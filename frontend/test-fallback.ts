import resolveDisplaySeries from './src/AddSeries/AddNewSeries/resolveDisplaySeries';

// mock out AddSeries and Series objects
const hubSeries = {
  id: 1,
  title: 'Season 1 (Hub)',
  aniDbId: 1,
  primaryMetadataProvider: 'anidb',
  aniDbMappings: [
    { aniDbId: 1, seasonNumber: 1 },
    { aniDbId: 2, seasonNumber: 2 },
  ],
  seasons: [
    { seasonNumber: 1, title: 'Season 1 (Hub)', images: [{ url: 'hub.jpg' }] },
    { seasonNumber: 2, title: 'Season 2 Local Title', images: [{ url: 'local2.jpg' }] },
  ]
} as any;

const searchResultSeason2 = {
  title: 'Season 2 Search Title',
  aniDbId: 2,
  primaryMetadataProvider: 'anidb',
  seasons: [], // no seasons, which mimics backend
  images: [{ url: 'search2.jpg' }]
} as any;

const searchResultSeason2MissingData = {
  title: 'Season 2 Search Title', // title is always present in search result
  aniDbId: 2,
  primaryMetadataProvider: 'anidb',
  seasons: []
} as any;

const hubSeriesMissingSeasonData = {
  ...hubSeries,
  seasons: [
    { seasonNumber: 1, title: 'Season 1 (Hub)' },
    { seasonNumber: 2 } // no title or images (mimicking pre-backfill)
  ]
} as any;

let exitCode = 0;

console.log('Test 1: Resolves to existingSeason local data');
const res1 = resolveDisplaySeries(searchResultSeason2, hubSeries);
if (res1.title === 'Season 2 Local Title' && res1.images?.[0]?.url === 'local2.jpg') {
  console.log('PASS');
} else {
  console.log('FAIL, got title:', res1.title, 'image:', res1.images?.[0]?.url);
  exitCode = 1;
}

console.log('\nTest 2: Falls back to search result data when existingSeason has no data');
const res2 = resolveDisplaySeries(searchResultSeason2MissingData, hubSeriesMissingSeasonData);
if (res2.title === 'Season 2 Search Title') {
  console.log('PASS');
} else {
  console.log('FAIL, got title:', res2.title);
  exitCode = 1;
}

if (exitCode !== 0) {
  throw new Error('Test failed with exit code ' + exitCode);
}
