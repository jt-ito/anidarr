using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Commands;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class RefreshSeriesServiceFixture : CoreTest<RefreshSeriesService>
    {
        private Series _series;

        [SetUp]
        public void Setup()
        {
            var season1 = Builder<Season>.CreateNew()
                                         .With(s => s.SeasonNumber = 1)
                                         .Build();

            _series = Builder<Series>.CreateNew()
                                     .With(s => s.Status = SeriesStatusType.Continuing)
                                     .With(s => s.Seasons = new List<Season>
                                                            {
                                                                season1
                                                            })
                                     .Build();

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetSeries(_series.Id))
                  .Returns(_series);

            Mocker.GetMock<IMetadataDispatcher>()
                  .Setup(s => s.GetSeriesInfo(It.IsAny<Series>()))
                  .Callback<Series>(p => { throw new SeriesNotFoundException(p.TvdbId); });

            Mocker.GetMock<IAutoTaggingService>()
                .Setup(s => s.GetTagChanges(_series))
                .Returns(new AutoTaggingChanges());
        }

        private void GivenNewSeriesInfo(Series series)
        {
            Mocker.GetMock<IMetadataDispatcher>()
                  .Setup(s => s.GetSeriesInfo(It.IsAny<Series>()))
                  .Returns(new Tuple<Series, List<Episode>>(series, new List<Episode>()));
        }

        [Test]
        public void should_monitor_new_seasons_automatically_if_monitor_new_items_is_all()
        {
            _series.MonitorNewItems = NewItemMonitorTypes.All;

            var newSeriesInfo = _series.JsonClone();
            newSeriesInfo.Seasons.Add(Builder<Season>.CreateNew()
                                         .With(s => s.SeasonNumber = 2)
                                         .Build());

            GivenNewSeriesInfo(newSeriesInfo);

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.Seasons.Count == 2 && s.Seasons.Single(season => season.SeasonNumber == 2).Monitored == true), It.IsAny<bool>(), It.IsAny<bool>()));
        }

        [Test]
        public void should_not_monitor_new_seasons_automatically_if_monitor_new_items_is_none()
        {
            _series.MonitorNewItems = NewItemMonitorTypes.None;

            var newSeriesInfo = _series.JsonClone();
            newSeriesInfo.Seasons.Add(Builder<Season>.CreateNew()
                .With(s => s.SeasonNumber = 2)
                .Build());

            GivenNewSeriesInfo(newSeriesInfo);

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.Seasons.Count == 2 && s.Seasons.Single(season => season.SeasonNumber == 2).Monitored == false), It.IsAny<bool>(), It.IsAny<bool>()));
        }

        [Test]
        public void should_not_monitor_new_special_season_automatically()
        {
            var series = _series.JsonClone();
            series.Seasons.Add(Builder<Season>.CreateNew()
                                         .With(s => s.SeasonNumber = 0)
                                         .Build());

            GivenNewSeriesInfo(series);

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.Seasons.Count == 2 && s.Seasons.Single(season => season.SeasonNumber == 0).Monitored == false), It.IsAny<bool>(), It.IsAny<bool>()));
        }

        [Test]
        public void should_stop_after_merging_a_series_that_was_a_later_season_of_a_hub()
        {
            _series.LastInfoSync = null;
            _series.AniDbId = 4738;

            var hubInfo = _series.JsonClone();
            hubInfo.AniDbId = 4337;
            GivenNewSeriesInfo(hubInfo);

            Mocker.GetMock<IAniDbHubReconciler>()
                  .Setup(r => r.Reconcile(It.IsAny<Series>(), It.IsAny<Series>()))
                  .Returns(HubOutcome.MergedIntoExisting);

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }, true));

            // the series no longer exists: nothing to save or scan
            Mocker.GetMock<ISeriesService>().Verify(v => v.UpdateSeries(It.IsAny<Series>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskScanService>().Verify(v => v.Scan(It.IsAny<Series>()), Times.Never());

            // it is done, even though it was merged away
            Mocker.GetMock<IPendingMetadataTracker>().Verify(t => t.Completed(), Times.Once());
            Mocker.GetMock<IManageCommandQueue>().Verify(c => c.Push(It.IsAny<EnrichSeriesFromAniListCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void should_carry_on_normally_when_a_series_became_its_hub()
        {
            _series.LastInfoSync = null;
            _series.AniDbId = 4738;

            GivenNewSeriesInfo(_series.JsonClone());

            Mocker.GetMock<IAniDbHubReconciler>()
                  .Setup(r => r.Reconcile(It.IsAny<Series>(), It.IsAny<Series>()))
                  .Returns(HubOutcome.BecameHub);

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }, true));

            Mocker.GetMock<ISeriesService>().Verify(v => v.UpdateSeries(It.IsAny<Series>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.AtLeastOnce());
            Mocker.GetMock<IDiskScanService>().Verify(v => v.Scan(It.IsAny<Series>()), Times.Once());

            Mocker.GetMock<IPendingMetadataTracker>().Verify(t => t.Completed(), Times.Once());

            // enrichment waits until the hub is known, and goes behind the refreshes still queued
            Mocker.GetMock<IManageCommandQueue>().Verify(c => c.Push(It.Is<EnrichSeriesFromAniListCommand>(cmd => cmd.SeriesId == _series.Id), CommandPriority.Low, CommandTrigger.Unspecified), Times.Once());
        }

        [Test]
        public void should_not_look_for_a_hub_when_the_series_was_fully_synced()
        {
            _series.LastInfoSync = DateTime.UtcNow.AddDays(-1);
            _series.AniDbId = 4738;

            GivenNewSeriesInfo(_series.JsonClone());

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<IAniDbHubReconciler>().Verify(r => r.Reconcile(It.IsAny<Series>(), It.IsAny<Series>()), Times.Never());
            Mocker.GetMock<IPendingMetadataTracker>().Verify(t => t.Completed(), Times.Never());
        }

        [Test]
        public void should_update_tvrage_id_if_changed()
        {
            var newSeriesInfo = _series.JsonClone();
            newSeriesInfo.TvRageId = _series.TvRageId + 1;

            GivenNewSeriesInfo(newSeriesInfo);

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.TvRageId == newSeriesInfo.TvRageId), It.IsAny<bool>(), It.IsAny<bool>()));
        }

        [Test]
        public void should_update_tvmaze_id_if_changed()
        {
            var newSeriesInfo = _series.JsonClone();
            newSeriesInfo.TvMazeId = _series.TvMazeId + 1;

            GivenNewSeriesInfo(newSeriesInfo);

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.TvMazeId == newSeriesInfo.TvMazeId), It.IsAny<bool>(), It.IsAny<bool>()));
        }

        [Test]
        public void should_update_tmdb_id_if_changed()
        {
            var newSeriesInfo = _series.JsonClone();
            newSeriesInfo.TmdbId = _series.TmdbId + 1;

            GivenNewSeriesInfo(newSeriesInfo);

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.TmdbId == newSeriesInfo.TmdbId), It.IsAny<bool>(), It.IsAny<bool>()));
        }

        [Test]
        public void should_log_error_if_tvdb_id_not_found()
        {
            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.Status == SeriesStatusType.Deleted), It.IsAny<bool>(), It.IsAny<bool>()), Times.Once());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_mark_as_deleted_if_tvdb_id_not_found()
        {
            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.Status == SeriesStatusType.Deleted), It.IsAny<bool>(), It.IsAny<bool>()), Times.Once());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_remark_as_deleted_if_tvdb_id_not_found()
        {
            _series.Status = SeriesStatusType.Deleted;

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                .Verify(v => v.UpdateSeries(It.IsAny<Series>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_update_if_tvdb_id_changed()
        {
            var newSeriesInfo = _series.JsonClone();
            newSeriesInfo.TvdbId = _series.TvdbId + 1;

            GivenNewSeriesInfo(newSeriesInfo);

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.TvdbId == newSeriesInfo.TvdbId), It.IsAny<bool>(), It.IsAny<bool>()));

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_throw_if_duplicate_season_is_in_existing_info()
        {
            var newSeriesInfo = _series.JsonClone();
            newSeriesInfo.Seasons.Add(Builder<Season>.CreateNew()
                                         .With(s => s.SeasonNumber = 2)
                                         .Build());

            _series.Seasons.Add(Builder<Season>.CreateNew()
                                         .With(s => s.SeasonNumber = 2)
                                         .Build());

            _series.Seasons.Add(Builder<Season>.CreateNew()
                                         .With(s => s.SeasonNumber = 2)
                                         .Build());

            GivenNewSeriesInfo(newSeriesInfo);

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                  .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.Seasons.Count == 2), It.IsAny<bool>(), It.IsAny<bool>()));
        }

        [Test]
        public void should_filter_duplicate_seasons()
        {
            var newSeriesInfo = _series.JsonClone();
            newSeriesInfo.Seasons.Add(Builder<Season>.CreateNew()
                                         .With(s => s.SeasonNumber = 2)
                                         .Build());

            newSeriesInfo.Seasons.Add(Builder<Season>.CreateNew()
                                         .With(s => s.SeasonNumber = 2)
                                         .Build());

            GivenNewSeriesInfo(newSeriesInfo);

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                  .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.Seasons.Count == 2), It.IsAny<bool>(), It.IsAny<bool>()));
        }

        [Test]
        public void should_rescan_series_if_updating_fails()
        {
            Mocker.GetMock<IMetadataDispatcher>()
                  .Setup(s => s.GetSeriesInfo(It.IsAny<Series>()))
                  .Throws(new IOException());

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<IDiskScanService>()
                  .Verify(v => v.Scan(_series), Times.Once());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_rescan_series_if_updating_fails_with_series_not_found()
        {
            Mocker.GetMock<IMetadataDispatcher>()
                  .Setup(s => s.GetSeriesInfo(It.IsAny<Series>()))
                  .Throws(new SeriesNotFoundException(_series.Id));

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<IDiskScanService>()
                  .Verify(v => v.Scan(_series), Times.Never());

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
