
var __assign = (this && this.__assign) || function() {
  __assign = Object.assign || function(t) {
    for (var s, i = 1, n = arguments.length; i < n; i++) {
      s = arguments[i];

      for (const p in s) {
        if (Object.prototype.hasOwnProperty.call(s, p)) {
          t[p] = s[p];
        }
      }
    }

    return t;
  };

  return __assign.apply(this, arguments);
};

Object.defineProperty(exports, '__esModule', { value: true });
exports.default = resolveDisplaySeries;

function resolveSeasonImages(existingSeason, series, existingSeries) {
  let _a; let
    _b;

  if ((_a = existingSeason === null || existingSeason === void 0 ? void 0 : existingSeason.images) === null || _a === void 0 ? void 0 : _a.length) {
    return existingSeason.images;
  }

  if ((_b = series.images) === null || _b === void 0 ? void 0 : _b.length) {
    return series.images;
  }

  return existingSeries.images;
}

function resolveDisplaySeries(series, existingSeries) {
  let _a; let _b; let
    _c;
  let seasonNumber = (_b = (_a = series.seasons) === null || _a === void 0 ? void 0 : _a[0]) === null || _b === void 0 ? void 0 : _b.seasonNumber;

  if (seasonNumber === undefined &&
        series.primaryMetadataProvider === 'anidb' &&
        series.aniDbId &&
        (existingSeries === null || existingSeries === void 0 ? void 0 : existingSeries.aniDbMappings)) {
    const mapping = existingSeries.aniDbMappings.find((m) => {
      return m.aniDbId === series.aniDbId;
    });

    if (mapping) {
      seasonNumber = mapping.seasonNumber;
    }
  }

  const existingSeason = seasonNumber === undefined ?
    undefined :
    (_c = existingSeries === null || existingSeries === void 0 ? void 0 : existingSeries.seasons) === null || _c === void 0 ? void 0 : _c.find((s) => {
      return s.seasonNumber === seasonNumber;
    });

  if (!existingSeries) {
    return series;
  }

  return __assign(__assign({}, existingSeries), { title: (existingSeason === null || existingSeason === void 0 ? void 0 : existingSeason.title) || series.title || existingSeries.title, images: resolveSeasonImages(existingSeason, series, existingSeries) });
}
