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
    273,
    320,
    [],
    'f000',
    'M273 298 L270 291 L260 282 L69 292 L63 295 L60 306 L67 318 L75 320 L261 319 L268 316 L272 311 Z M251 259 L250 253 L243 243 L234 239 L224 238 L66 270 L63 273 L63 278 L66 283 L236 273 L247 271 Z M220 216 L214 208 L202 203 L196 203 L73 244 L68 247 L66 257 L68 260 L216 230 L220 227 Z M174 199 L163 193 L145 188 L123 188 L108 192 L94 200 L85 209 L78 221 L76 232 Z M75 86 L63 93 L51 96 L89 184 L93 188 L109 180 L116 179 L116 176 Z M38 0 L19 7 L10 15 L1 32 L0 50 L4 63 L15 77 L29 85 L48 87 L67 80 L80 67 L86 52 L86 35 L80 20 L70 9 L54 1 Z M50 10 L51 13 L48 18 L29 24 L16 43 L9 41 L10 30 L22 15 L37 8 L44 7 Z',
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
