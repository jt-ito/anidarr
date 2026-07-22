//
// Regular

import { IconDefinition } from '@fortawesome/fontawesome-svg-core';
import {
  faBookmark as farBookmark,
  faCalendar as farCalendar,
  faCircle as farCircle,
  faClock as farClock,
  faClone as farClone,
  faDotCircle as farDotCircle,
  faFile as farFile,
  faFileArchive as farFileArchive,
  faFileVideo as farFileVideo,
  faFolder as farFolder,
  faHdd as farHdd,
  faHeart as farHeart,
  faKeyboard as farKeyboard,
  faObjectGroup as farObjectGroup,
  faObjectUngroup as farObjectUngroup,
  faSquare as farSquare,
} from '@fortawesome/free-regular-svg-icons';
//
// Solid
import {
  faArrowCircleLeft as fasArrowCircleLeft,
  faArrowCircleRight as fasArrowCircleRight,
  faAsterisk as fasAsterisk,
  faBackward as fasBackward,
  faBan as fasBan,
  faBars as fasBars,
  faBolt as fasBolt,
  faBookmark as fasBookmark,
  faBookReader as fasBookReader,
  faBroadcastTower as fasBroadcastTower,
  faBug as fasBug,
  faCalculator as fasCalculator,
  faCalendarAlt as fasCalendarAlt,
  faCaretDown as fasCaretDown,
  faCheck as fasCheck,
  faCheckCircle as fasCheckCircle,
  faChevronCircleDown as fasChevronCircleDown,
  faChevronCircleRight as fasChevronCircleRight,
  faChevronCircleUp as fasChevronCircleUp,
  faCircle as fasCircle,
  faCircleDown as fasCircleDown,
  faCirclePause as fasCirclePause,
  faCirclePlay as fasCirclePlay,
  faCircleStop as fasCircleStop,
  faCloud as fasCloud,
  faCloudDownloadAlt as fasCloudDownloadAlt,
  faCog as fasCog,
  faCogs as fasCogs,
  faCopy as fasCopy,
  faDesktop as fasDesktop,
  faDownload as fasDownload,
  faEllipsisH as fasEllipsisH,
  faExclamationCircle as fasExclamationCircle,
  faExclamationTriangle as fasExclamationTriangle,
  faExternalLinkAlt as fasExternalLinkAlt,
  faEye as fasEye,
  faFastBackward as fasFastBackward,
  faFastForward as fasFastForward,
  faFileCircleQuestion as fasFileCircleQuestion,
  faFileExport as fasFileExport,
  faFileInvoice as farFileInvoice,
  faFilter as fasFilter,
  faFlag as fasFlag,
  faFolderOpen as fasFolderOpen,
  faFolderTree as farFolderTree,
  faForward as fasForward,
  faGlobe as fasGlobe,
  faHeart as fasHeart,
  faHistory as fasHistory,
  faHome as fasHome,
  faInfoCircle as fasInfoCircle,
  faLanguage as fasLanguage,
  faLaptop as fasLaptop,
  faLevelUpAlt as fasLevelUpAlt,
  faListCheck as fasListCheck,
  faMedkit as fasMedkit,
  faMinus as fasMinus,
  faPause as fasPause,
  faPlay as fasPlay,
  faPlus as fasPlus,
  faPowerOff as fasPowerOff,
  faQuestion as fasQuestion,
  faQuestionCircle as fasQuestionCircle,
  faRedoAlt as fasRedoAlt,
  faRetweet as fasRetweet,
  faRocket as fasRocket,
  faRss as fasRss,
  faSave as fasSave,
  faSearch as fasSearch,
  faSignOutAlt as fasSignOutAlt,
  faSitemap as fasSitemap,
  faSort as fasSort,
  faSortDown as fasSortDown,
  faSortUp as fasSortUp,
  faSpinner as fasSpinner,
  faSquareCheck as fasSquareCheck,
  faSquareMinus as fasSquareMinus,
  faStop as fasStop,
  faSync as fasSync,
  faTable as fasTable,
  faTags as fasTags,
  faTh as fasTh,
  faTheaterMasks as fasTheaterMasks,
  faThList as fasThList,
  faTimes as fasTimes,
  faTimesCircle as fasTimesCircle,
  faTrashAlt as fasTrashAlt,
  faUser as fasUser,
  faUserPlus as fasUserPlus,
  faVial as fasVial,
  faWrench as fasWrench,
} from '@fortawesome/free-solid-svg-icons';

//
// Icons

export const ACTIONS = fasBolt;
export const ACTIVITY = farClock;
export const ADD = fasPlus;
export const ALTERNATE_TITLES = farClone;
export const ADVANCED_SETTINGS = fasCog;
export const ARROW_LEFT = fasArrowCircleLeft;
export const ARROW_RIGHT = fasArrowCircleRight;
export const BACKUP = farFileArchive;
export const BLOCKLIST = fasBan;
export const BUG = fasBug;
export const CALENDAR = fasCalendarAlt;
export const CALENDAR_O = farCalendar;
export const CARET_DOWN = fasCaretDown;
export const CHECK = fasCheck;
export const CHECK_INDETERMINATE = fasMinus;
export const CHECK_CIRCLE = fasCheckCircle;
export const CHECK_SQUARE = fasSquareCheck;
export const CIRCLE = fasCircle;
export const CIRCLE_DOWN = fasCircleDown;
export const CIRCLE_OUTLINE = farCircle;
export const CLEAR = fasTrashAlt;
export const CLIPBOARD = fasCopy;
export const CLOSE = fasTimes;
export const CLONE = farClone;
export const COLLAPSE = fasChevronCircleUp;
export const COMPUTER = fasDesktop;
export const DANGER = fasExclamationCircle;
export const DELETE = fasTrashAlt;
export const DOWNLOAD = fasDownload;
export const DOWNLOADED = fasDownload;
export const DOWNLOADING = fasCloudDownloadAlt;
export const DRIVE = farHdd;
export const EDIT = fasWrench;
export const EPISODE_FILE = farFileVideo;
export const EXPAND = fasChevronCircleDown;
export const EXPAND_INDETERMINATE = fasChevronCircleRight;
export const EXPORT = fasFileExport;
export const EXTERNAL_LINK = fasExternalLinkAlt;
export const FATAL = fasTimesCircle;
export const FILE = farFile;
export const FILE_MISSING = fasFileCircleQuestion;
export const FILTER = fasFilter;
export const FINALE_SEASON = fasCirclePause;
export const FINALE_SERIES = fasCircleStop;
export const FLAG = fasFlag;
export const FOOTNOTE = fasAsterisk;
export const FOLDER = farFolder;
export const FOLDER_OPEN = fasFolderOpen;
export const GENRE = fasTheaterMasks;
export const GLOBE = fasGlobe;
export const GROUP = farObjectGroup;
export const HEALTH = fasMedkit;
export const HEART = fasHeart;
export const HEART_OUTLINE = farHeart;
export const HISTORY = fasHistory;
export const HOUSEKEEPING = fasHome;
export const IGNORE = fasTimesCircle;
export const INFO = fasInfoCircle;
export const INTERACTIVE = fasUser;
export const KEYBOARD = farKeyboard;

export const MANUAL_IMPORT: IconDefinition = {
  prefix: 'fas',
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  iconName: 'manual-import' as any,
  icon: [
    612,
    612,
    [],
    'f000',
    'M442 442 L439 435 L429 426 L238 436 L232 439 L229 450 L236 462 L244 464 L430 463 L437 460 L441 455 Z M420 403 L419 397 L412 387 L403 383 L393 382 L235 414 L232 417 L232 422 L235 427 L405 417 L416 415 Z M389 360 L383 352 L371 347 L365 347 L242 388 L237 391 L235 401 L237 404 L385 374 L389 371 Z M343 343 L332 337 L314 332 L292 332 L277 336 L263 344 L254 353 L247 365 L245 376 Z M244 230 L232 237 L220 240 L258 328 L262 332 L278 324 L285 323 L285 320 Z M207 144 L188 151 L179 159 L170 176 L169 194 L173 207 L184 221 L198 229 L217 231 L236 224 L249 211 L255 196 L255 179 L249 164 L239 153 L223 145 Z M219 154 L220 157 L217 162 L198 168 L185 187 L178 185 L179 174 L191 159 L206 152 L213 151 Z',
  ],
};

export const LANGUAGE = fasLanguage;
export const LOGOUT = fasSignOutAlt;
export const MANAGE = fasListCheck;
export const MEDIA_INFO = farFileInvoice;
export const MISSING = fasExclamationTriangle;
export const MONITORED = fasBookmark;
export const NETWORK = fasBroadcastTower;
export const NAVBAR_COLLAPSE = fasBars;
export const NOT_AIRED = farClock;
export const ORGANIZE = fasSitemap;
export const OVERFLOW = fasEllipsisH;
export const OVERVIEW = fasThList;
export const PAGE_FIRST = fasFastBackward;
export const PAGE_PREVIOUS = fasBackward;
export const PAGE_NEXT = fasForward;
export const PAGE_LAST = fasFastForward;
export const PARENT = fasLevelUpAlt;
export const PARSE = fasCalculator;
export const PAUSED = fasPause;
export const PENDING = farClock;
export const PREMIERE = fasCirclePlay;
export const PROFILE = fasUser;
export const POSTER = fasTh;
export const QUEUED = fasCloud;
export const QUICK = fasRocket;
export const REFRESH = fasSync;
export const REMOVE = fasTimes;
export const RESTART = fasRedoAlt;
export const RESTORE = fasHistory;
export const REORDER = fasBars;
export const ROOT_FOLDER = farFolderTree;
export const RSS = fasRss;
export const SAVE = fasSave;
export const SCENE_MAPPING = fasSitemap;
export const SCHEDULED = farClock;
export const SCORE = fasUserPlus;
export const SEARCH = fasSearch;
export const SERIES_CONTINUING = fasPlay;
export const SERIES_ENDED = fasStop;
export const SERIES_DELETED = fasExclamationTriangle;
export const SETTINGS = fasCogs;
export const SHUTDOWN = fasPowerOff;
export const SORT = fasSort;
export const SORT_ASCENDING = fasSortUp;
export const SORT_DESCENDING = fasSortDown;
export const SPINNER = fasSpinner;
export const SQUARE = farSquare;
export const SQUARE_MINUS = fasSquareMinus;
export const SUBTRACT = fasMinus;
export const SYSTEM = fasLaptop;
export const TABLE = fasTable;
export const TAGS = fasTags;
export const TBA = fasQuestionCircle;
export const TEST = fasVial;
export const UNGROUP = farObjectUngroup;
export const UNKNOWN = fasQuestion;
export const UNMONITORED = farBookmark;
export const UPDATE = fasRetweet;
export const UNSAVED_SETTING = farDotCircle;
export const VIEW = fasEye;
export const WARNING = fasExclamationTriangle;
export const WIKI = fasBookReader;
