<div align="center">

<img src="logo.png" alt="Anidarr Logo" width="200" height="200" style="display: none;"/>

# Anidarr

**The Ultimate PVR for Anime Enthusiasts.**

[![GitHub License](https://img.shields.io/badge/license-GPLv3-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-18-61DAFB.svg)](https://reactjs.org/)

Anidarr is a specialized fork of [Sonarr](https://github.com/Sonarr/Sonarr) designed from the ground up to solve the unique challenges of organizing and automating anime collections. It bypasses TVDB and Skyhook, directly integrating with native anime metadata providers to give you faster, more accurate, and more comprehensive anime indexing.

</div>

---

## ✨ Features

Anidarr brings several anime-first features that aren't available in standard Sonarr:

### 🎭 Multi-Provider Metadata
Why rely on just one source? Anidarr supports **AniDB** and **Simkl** as primary metadata providers. You can easily select your preferred source in the settings, allowing you to fetch the absolute best metadata for your collection without relying on error-prone community-maintained mapping databases.

### ⚡ Lightning-Fast Local Search
By integrating the [Anime Offline Database](https://github.com/manami-project/anime-offline-database), searches for new anime happen locally via a cached SQLite database. This means zero API delays, no rate limits, and instant results when adding new series. Search results are automatically deduplicated across all supported providers so you always get a clean list.

### 🌐 Multi-Language Title Fallbacks
Anime titles can be tricky. Anidarr automatically falls back between English, Romaji, and Native (Japanese) titles so your library reads naturally. You can customize the title preference in the UI.

### 🛡️ Robust Rate Limiting & Handling
Built-in support for strict API limits (like AniDB's stringent throttling). Anidarr features a background health check and smart queuing mechanism that gracefully handles API limits and temporary bans without crashing or failing silently.

### 🏷️ Alternate Titles & Synonyms
Directly imports and displays synonyms and alternate titles on the series details page, drastically improving automated episode parsing and manual searches.

### 🎬 Anime-Centric Episode Management
Proper handling of Absolute Episode Numbers, OVA processing, and split-cour series parsing right out of the box.

---

## 🚀 Getting Started

Anidarr is built using .NET and React (via Yarn).

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) & Yarn

### Building from Source

1. **Clone the repository:**
   ```bash
   git clone https://github.com/jt-ito/anidarr.git
   cd anidarr
   ```

2. **Build the Backend:**
   ```bash
   dotnet build src/Sonarr.sln -c Debug
   ```

3. **Start the Backend Server:**
   ```bash
   ./_output/net10.0/Sonarr.Console.exe
   ```

4. **Install Frontend Dependencies & Start the Dev Server** (in a new terminal):
   ```bash
   cd frontend
   yarn install
   yarn start
   ```

5. **Access Anidarr:**
   Open your browser and navigate to `http://localhost:8989`.

---

## ⚙️ Configuration

Once installed, head over to **Settings > Metadata** to select your preferred primary metadata provider (AniDB or Simkl). The UI is custom-tailored to link directly to your chosen provider's pages instead of TVDB.

---

## 🤝 Contributing

Anidarr is an open-source project and we welcome contributions! Whether it's fixing bugs, adding new metadata providers, or improving the React frontend, feel free to open a Pull Request.

---

## 📜 License

Anidarr is a fork of Sonarr and inherits its GPL-3.0 License. See the [LICENSE](LICENSE) file for more details.
