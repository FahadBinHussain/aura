import requests
import logging
import shutil
from pathlib import Path
import os
import platform
import subprocess
import ctypes
import random
from python.utils import load_env_vars, load_config

# Load environment variables
load_env_vars()

# Load configuration
config = load_config()

# Pexels API key from environment variable
PEXELS_API_KEY = os.getenv("PEXELS_API_KEY")

# Check if API key is loaded
if not PEXELS_API_KEY:
    logging.error("Pexels API Key is missing! Please set it in the .env file.")

def fetch_pexels_wallpapers(query="wallpapers", count=10):
    """Fetch wallpapers from Pexels API."""
    random_page = random.randint(1, 100)  # Add a random page to ensure different results
    url = f"https://api.pexels.com/v1/search?query={query}&per_page={count}&page={random_page}"
    headers = {"Authorization": PEXELS_API_KEY}
    try:
        response = requests.get(url, headers=headers)
        response.raise_for_status()
        photos = response.json()["photos"]
        wallpapers = [{"id": photo["id"], "photographer": photo["photographer"], "url": photo["src"]["original"]} for photo in photos]
        logging.info(f"Fetched {len(wallpapers)} wallpapers from Pexels.")
        return wallpapers
    except requests.RequestException as e:
        logging.error(f"Failed to fetch from Pexels: {e}")
        return []

def save_pexels_wallpapers(wallpapers, directory):
    """Save wallpapers from URLs to the specified directory."""
    directory.mkdir(parents=True, exist_ok=True)
    
    for wallpaper in wallpapers:
        try:
            response = requests.get(wallpaper["url"], stream=True)
            response.raise_for_status()
            file_path = directory / f"{wallpaper['id']}_{wallpaper['photographer'].replace(' ', '_')}.jpg"
            with open(file_path, "wb") as f:
                shutil.copyfileobj(response.raw, f)
            logging.info(f"Saved wallpaper to {file_path}")
        except requests.RequestException as e:
            logging.error(f"Failed to download wallpaper from {wallpaper['url']}: {e}")

def set_pexels_wallpaper(file_path):
    """Set the wallpaper using a given file path depending on the OS."""
    system = platform.system()
    if system == "Windows":
        set_wallpaper_windows(file_path)
    elif system == "Darwin":  # macOS
        set_wallpaper_macos(file_path)
    elif system == "Linux":
        set_wallpaper_linux(file_path)
    else:
        logging.error(f"Unsupported operating system: {system}")

def set_wallpaper_windows(file_path):
    """Set the desktop wallpaper on Windows."""
    absolute_path = os.path.abspath(file_path)
    ctypes.windll.user32.SystemParametersInfoW(20, 0, absolute_path, 3)
    logging.info(f"Wallpaper set to {absolute_path} on Windows")

def set_wallpaper_macos(file_path):
    """Set the desktop wallpaper on macOS."""
    script = f'''
    tell application "System Events"
        set picture of every desktop to "{file_path}"
    end tell
    '''
    subprocess.run(["osascript", "-e", script])
    logging.info(f"Wallpaper set to {file_path} on macOS")

def set_wallpaper_linux(file_path):
    """Set the desktop wallpaper on Linux with GNOME."""
    subprocess.run(["gsettings", "set", "org.gnome.desktop.background", "picture-uri", f"file://{file_path}"])
    logging.info(f"Wallpaper set to {file_path} on Linux")

# Example usage:
# if __name__ == "__main__":
#     wallpapers = fetch_pexels_wallpapers()
#     save_wallpapers(wallpapers, Path(config['SAVE_LOCATION']) / "pexels_wallpapers")
#     latest_wallpaper = get_latest_wallpaper(Path(config['SAVE_LOCATION']) / "pexels_wallpapers")
#     if latest_wallpaper:
#         set_wallpaper(latest_wallpaper)