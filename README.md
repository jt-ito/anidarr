<div align="center">

# Anidarr

**A fork of Sonarr specialized for Anime.**

</div>

Anidarr is a specialized fork of [Sonarr](https://github.com/Sonarr/Sonarr) designed from the ground up for anime enthusiasts. It bypasses TVDB and Skyhook, directly integrating with **AniDB** and the **Anime Offline Database** for faster, more accurate anime metadata.

---

## ✨ Features

- **Native AniDB Integration:** Anidarr uses AniDB as its primary metadata provider, ensuring anime series are indexed correctly without relying on community-maintained mapping databases.
- **Lightning-Fast Local Search:** By integrating the [Anime Offline Database](https://github.com/manami-project/anime-offline-database), searches for new anime happen locally via a cached SQLite database, avoiding rate limits and API delays.
- **Multi-Language Title Fallbacks:** Automatically falls back to English -> Romaji -> Native (Japanese) titles so your library reads naturally.
- **Robust Rate Limiting:** Built-in support for AniDB's strict API limits, featuring a background health check and throttling mechanism that gracefully handles temporary API bans.
- **Alternate Titles & Synonyms:** Directly imports and displays AniDB synonyms and alternate titles on the series details page.
- **Custom Anime UI:** Modified frontend UI links directly to AniDB pages instead of TVDB.

## 🚀 Getting Started

Anidarr is built using .NET and React (via Yarn).

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) & Yarn

### Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/jt-ito/anidarr.git
   cd anidarr
   ```

2. Build the Backend:
   ```bash
   dotnet build src/Sonarr.sln -c Debug
   ```

3. Start the Backend Server:
   ```bash
   ./_output/net10.0/Sonarr.Console.exe
   ```

4. Install Frontend Dependencies & Start the Dev Server (in a new terminal):
   ```bash
   cd frontend
   yarn install
   yarn start
   ```

5. Access Anidarr in your browser at `http://localhost:8989`.

## 📜 License

Anidarr is a fork of Sonarr and inherits its GPL-3.0 License. See the [LICENSE](LICENSE) file for more details.
