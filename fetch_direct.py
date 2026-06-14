import json
import requests

TARGET_URL = "https://www.cctrcloud.net/practice/subject_practice.html?studentpractise_id=2711631&a=0&practiseid=25131&courseid=111393&teacherid=27004&coursename=2026%25E6%2598%25A5%25E6%25AF%259B%25E6%25A6%25822&studentpractisequestioncount=184&isaiquestion=0"

def try_fetch():
    with open('config.json', 'r', encoding='utf-8') as f:
        config = json.load(f)
    
    token = config.get('jwt_token', '')
    
    headers = {
        "Authorization": f"Bearer {token}",
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
    }
    
    # Try fetching the page itself to see if questions are embedded
    print("Fetching page...")
    resp = requests.get(TARGET_URL, headers=headers)
    print(f"Status: {resp.status_code}")
    
    with open("page_dump.html", "w", encoding="utf-8") as f:
        f.write(resp.text)
        
    print("Done. Saved to page_dump.html")

if __name__ == "__main__":
    try_fetch()
