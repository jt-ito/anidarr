
const series = [{ tvdbId: 0, aniDbId: 15274, tmdbId: 0, simklId: 0, malIds: [], aniListIds: [] }];
const seriesToCheck = { tvdbId: 373728, aniDbId: 0, tmdbId: 95897, simklId: 0, malIds: [], aniListIds: [] };

function isExisting(seriesToCheck, s) {
    if (seriesToCheck.tvdbId && seriesToCheck.tvdbId > 0 && s.tvdbId === seriesToCheck.tvdbId) return true;
    if (seriesToCheck.aniDbId && seriesToCheck.aniDbId > 0 && s.aniDbId === seriesToCheck.aniDbId) return true;
    if (seriesToCheck.tmdbId && seriesToCheck.tmdbId > 0 && s.tmdbId === seriesToCheck.tmdbId) return true;
    if (seriesToCheck.simklId && seriesToCheck.simklId > 0 && s.simklId === seriesToCheck.simklId) return true;
    
    if (seriesToCheck.malIds && seriesToCheck.malIds.length > 0 && s.malIds && s.malIds.length > 0) {
    if (seriesToCheck.malIds.some(id => id > 0 && s.malIds.includes(id))) return true;
    }
    
    if (seriesToCheck.aniListIds && seriesToCheck.aniListIds.length > 0 && s.aniListIds && s.aniListIds.length > 0) {
    if (seriesToCheck.aniListIds.some(id => id > 0 && s.aniListIds.includes(id))) return true;
    }

    return false;
}

console.log(series.some(s => isExisting(seriesToCheck, s)));
