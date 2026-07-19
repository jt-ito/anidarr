#!/bin/bash
set -e

# Default to running as root if no PUID is provided
USER_ID=${PUID:-0}
GROUP_ID=${PGID:-0}

if [ "$USER_ID" -ne 0 ] || [ "$GROUP_ID" -ne 0 ]; then
    echo "Running as user ID $USER_ID and group ID $GROUP_ID"

    # Create the anidarr group if it doesn't exist
    if ! getent group anidarr >/dev/null; then
        groupadd -o -g "$GROUP_ID" anidarr
    fi

    # Create the anidarr user if it doesn't exist
    if ! getent passwd anidarr >/dev/null; then
        useradd -o -u "$USER_ID" -g "$GROUP_ID" -s /bin/sh -d /app anidarr
    fi

    # Fix permissions for the config directory so the user can read/write to it
    # Optimize by checking if it's already correct to save time on huge directories
    if [ "$(stat -c '%u:%g' /config)" != "$USER_ID:$GROUP_ID" ]; then
        echo "Updating permissions for /config..."
        chown -R anidarr:anidarr /config
    fi

    # Drop privileges and execute the CMD using gosu
    exec gosu anidarr "$@"
else
    echo "WARNING: Running as root. It is highly recommended to set PUID and PGID for better container security."
    exec "$@"
fi
