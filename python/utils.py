import os
import json
from dotenv import load_dotenv
import logging
import threading
import sys

config_lock = threading.Lock()

def load_env_vars():
    """Load environment variables from .env file."""
    if getattr(sys, 'frozen', False):
        # Running as compiled exe
        exe_dir = os.path.dirname(sys.executable)
        env_path = os.path.join(exe_dir, '.env')
    else:
        # Running as script
        env_path = os.path.join(os.path.dirname(__file__), '.env')
    
    if not os.path.exists(env_path):
        with open(env_path, 'w') as f:
            f.write("# Add your API keys below\nUNSPLASH_ACCESS_KEY=\nPEXELS_API_KEY=\n")
    
    load_dotenv(env_path)

def load_config():
    with config_lock:
        if getattr(sys, 'frozen', False):
            # Running as exe - use executable directory
            base_path = os.path.dirname(sys.executable)
        else:
            base_path = os.path.dirname(os.path.abspath(__file__))
        
        config_path = os.path.join(base_path, 'config.json')
        
        if not os.path.exists(config_path):
            default_config = {
                "SAVE_LOCATION": os.path.join(os.path.expanduser("~"), "WallYouNeed"),
                "SOURCE_UNSPLASH": False,
                "SOURCE_PEXELS": False,
                "SOURCE_WALLPAPER_ENGINE": False,
                "CHECK_INTERVAL": "300",
                "COLLECTIONS_URL": "https://steamcommunity.com/sharedfiles/filedetails/?id=2801058904",
                "WALLPAPER_DOWNLOAD_LIMIT": "1",
                "MAX_WALLPAPERS": "1",
                "SAVE_OLD_WALLPAPERS": False,
                "UPDATE_RUNNING": False
            }
            with open(config_path, 'w') as f:
                json.dump(default_config, f, indent=4)
        
        try:
            with open(config_path, 'r') as f:
                config = json.load(f)
            # logging.info(f"Configuration loaded successfully: {config}")
            logging.info(f"Configuration loaded successfully")
        except json.JSONDecodeError as e:
            logging.error(f"Error decoding JSON: {e}")

        return config

def save_config(config):
    with config_lock:
        try:
            # Get the correct config path using same logic as load_config()
            base_path = os.path.dirname(sys.executable) if getattr(sys, 'frozen', False) \
                else os.path.dirname(os.path.abspath(__file__))
            config_path = os.path.join(base_path, 'config.json')
            
            with open(config_path, 'w') as f:
                json.dump(config, f, indent=4)
            logging.info("Configuration saved successfully.")
        except Exception as e:
            logging.error(f"Error saving configuration: {e}")

# # Load environment variables at the start of the script
# load_env_vars()

# # Example usage:
# if __name__ == "__main__":
#     config = load_config()
#     if config:
#         # print("Loaded configuration:", config)
#         print("Loaded configuration")
#     else:
#         print("Failed to load configuration.")
#     print("Loaded usernames:", os.getenv('USERNAMES'))