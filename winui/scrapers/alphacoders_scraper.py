import os
import requests
from bs4 import BeautifulSoup
import json
from concurrent.futures import ThreadPoolExecutor, as_completed

# --- Folders ---
small_folder = "small"
big_folder = "big"
original_folder = "original"
os.makedirs(small_folder, exist_ok=True)
os.makedirs(big_folder, exist_ok=True)
os.makedirs(original_folder, exist_ok=True)

# --- JSON files ---
small_json_file = "image_urls.json"
big_json_file = "big_image_urls.json"
original_json_file = "original_image_urls.json"

# --- Base URLs ---
small_base_url = "https://alphacoders.com/resolution/4k-wallpapers?page={}"

# --- Storage ---
all_small_urls = []
all_big_urls = []
all_original_urls = []

# --- Functions ---
def get_small_image_urls(page_number):
    url = small_base_url.format(page_number)
    headers = {"User-Agent": "Mozilla/5.0"}
    response = requests.get(url, headers=headers)
    soup = BeautifulSoup(response.text, "html.parser")
    
    image_urls = []
    for img_tag in soup.find_all("img"):
        src = img_tag.get("src")
        if src and "thumbbig" in src and src.startswith("https://images"):
            image_urls.append(src)
    return image_urls

def download_image(url, folder):
    filename = os.path.join(folder, url.split("/")[-1])
    if os.path.exists(filename):
        return filename
    try:
        with requests.get(url, stream=True) as r:
            if r.status_code == 200:
                with open(filename, "wb") as f:
                    for chunk in r.iter_content(1024):
                        f.write(chunk)
                print(f"Saved {filename}")
                return filename
    except Exception as e:
        print(f"Failed to download {url}: {e}")
    return None

def get_image_id_from_url(url):
    return url.split("-")[-1].split(".")[0]

def get_big_image_url_and_download(small_url):
    image_id = get_image_id_from_url(small_url)
    
    # Extract domain and folder from small image URL
    parts = small_url.split("/")
    domain = parts[2]          # e.g., images5.alphacoders.com
    folder_number = parts[3]   # e.g., 564
    
    base_big_url = f"https://{domain}/{folder_number}/thumb-1920-{image_id}"
    
    for ext in ["jpeg", "jpg", "png"]:
        big_url = f"{base_big_url}.{ext}"
        filename = os.path.join(big_folder, f"thumb-1920-{image_id}.{ext}")
        if os.path.exists(filename):
            return big_url
        try:
            r = requests.get(big_url, stream=True)
            if r.status_code == 200:
                with open(filename, "wb") as f:
                    for chunk in r.iter_content(1024):
                        f.write(chunk)
                print(f"Saved {filename}")
                return big_url
        except Exception as e:
            print(f"Failed {big_url}: {e}")
    print(f"Big image not found for {small_url}")
    return None

def get_original_image_url_and_download(small_url):
    image_id = get_image_id_from_url(small_url)
    
    parts = small_url.split("/")
    domain_short = parts[2].split(".")[0]  # e.g., images3
    
    extensions = ["jpeg", "jpg", "png"]
    
    for ext in extensions:
        original_url = f"https://initiate.alphacoders.com/download/{domain_short}/{image_id}/{ext}"
        filename = os.path.join(original_folder, f"{image_id}.{ext}")
        if os.path.exists(filename):
            return original_url
        try:
            r = requests.get(
                original_url,
                headers={"User-Agent": "Mozilla/5.0", "Referer": "https://wall.alphacoders.com/"},
                stream=True
            )
            if r.status_code == 200:
                with open(filename, "wb") as f:
                    for chunk in r.iter_content(1024):
                        f.write(chunk)
                print(f"Saved original {filename}")
                return original_url
        except Exception as e:
            print(f"Failed {original_url}: {e}")
    
    print(f"Original image not found for {small_url}")
    return None

# --- Part 1: Small Images ---
for page in range(1, 3):
    small_urls = get_small_image_urls(page)
    print(f"Found {len(small_urls)} small images on page {page}")
    all_small_urls.extend(small_urls)

# Download small images in parallel
with ThreadPoolExecutor(max_workers=10) as executor:
    futures = [executor.submit(download_image, url, small_folder) for url in all_small_urls]
    for future in as_completed(futures):
        future.result()

# Save small URLs
with open(small_json_file, "w") as f:
    json.dump(all_small_urls, f, indent=4)
print(f"Saved all small image URLs to {small_json_file}")

# --- Part 2 & 3: Big and Original Images ---
with ThreadPoolExecutor(max_workers=10) as executor:
    futures = []
    for small_url in all_small_urls:
        futures.append(executor.submit(get_big_image_url_and_download, small_url))
        futures.append(executor.submit(get_original_image_url_and_download, small_url))
    
    for future in as_completed(futures):
        result = future.result()
        if result:
            if "thumb-1920" in result:
                all_big_urls.append(result)
            else:
                all_original_urls.append(result)

# Save big URLs
with open(big_json_file, "w") as f:
    json.dump(all_big_urls, f, indent=4)
print(f"Saved all big image URLs to {big_json_file}")

# Save original URLs
with open(original_json_file, "w") as f:
    json.dump(all_original_urls, f, indent=4)
print(f"Saved all original image URLs to {original_json_file}")
