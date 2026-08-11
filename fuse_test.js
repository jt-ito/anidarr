const fuseModule = require('./node_modules/fuse.js/dist/fuse.cjs');
const Fuse = fuseModule.default || fuseModule;

const data = [
  {
    alternateTitles: [
      { title: 'Naruto' }
    ]
  },
  {
    alternateTitles: [
      { title: 'Kare no Shiranai Himitsu o Irete. The Animation' }
    ]
  }
];

const options = {
  keys: ['alternateTitles.title'],
  threshold: 0.3
};

const fuse = new Fuse(data, options);

const result1 = fuse.search('Narto');
console.log('Search for "Narto":', JSON.stringify(result1, null, 2));

const result2 = fuse.search('Kare no Shiranai Himitsu wo Irete. The Animation');
console.log('Search for "...wo Irete...":', JSON.stringify(result2, null, 2));

