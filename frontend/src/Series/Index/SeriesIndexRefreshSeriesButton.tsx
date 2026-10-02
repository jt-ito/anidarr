import React, { useCallback } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import { icons } from 'Helpers/Props';
import Series from 'Series/Series';
import useMetadataProgress from 'Series/useMetadataProgress';
import { useSeriesIndex } from 'Series/useSeries';
import translate from 'Utilities/String/translate';

interface SeriesIndexRefreshSeriesButtonProps {
  isSelectMode: boolean;
  selectedFilterKey: string | number;
}

function SeriesIndexRefreshSeriesButton(
  props: SeriesIndexRefreshSeriesButtonProps
) {
  const isRefreshing = useCommandExecuting(CommandNames.RefreshSeries);
  const { data, totalItems } = useSeriesIndex();
  const metadata = useMetadataProgress();

  const executeCommand = useExecuteCommand();
  const { isSelectMode, selectedFilterKey } = props;
  const { anySelected, getSelectedIds } = useSelect<Series>();

  let refreshLabel = translate('UpdateAll');

  if (anySelected) {
    refreshLabel = translate('UpdateSelected');
  } else if (selectedFilterKey !== 'all') {
    refreshLabel = translate('UpdateFiltered');
  }

  // Series added "now, details later" are still being updated from AniDB in the background (TVDB
  // series are never part of this). Spin while that goes on and say how far it has got.
  const needsClient = metadata.clientConfigured === false;
  const isBlocked = !!metadata.blockedUntil;
  const isUpdatingMetadata = metadata.pending > 0 && !isBlocked && !needsClient;
  let metadataTooltip: string | undefined = undefined;

  if (metadata.pending > 0 && needsClient) {
    metadataTooltip = translate('UpdatingSeriesNeedsClient', {
      pending: metadata.pending,
    });
  } else if (isUpdatingMetadata) {
    metadataTooltip = translate('UpdatingSeriesProgress', {
      done: metadata.done,
      total: metadata.total,
    });
  } else if (metadata.pending > 0 && metadata.blockedUntil) {
    metadataTooltip = translate('UpdatingSeriesBlocked', {
      pending: metadata.pending,
      time: new Date(metadata.blockedUntil).toLocaleTimeString(),
    });
  }

  const onPress = useCallback(() => {
    const seriesToRefresh =
      isSelectMode && anySelected ? getSelectedIds() : data.map((m) => m.id);

    executeCommand({
      name: CommandNames.RefreshSeries,
      seriesIds: seriesToRefresh,
    });
  }, [executeCommand, anySelected, isSelectMode, data, getSelectedIds]);

  return (
    <PageToolbarButton
      label={refreshLabel}
      isSpinning={isRefreshing || isUpdatingMetadata}
      isDisabled={!totalItems}
      iconName={icons.REFRESH}
      {...(metadataTooltip ? { title: metadataTooltip } : {})}
      onPress={onPress}
    />
  );
}

export default SeriesIndexRefreshSeriesButton;
