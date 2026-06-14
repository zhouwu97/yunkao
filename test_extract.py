import os
from bs4 import BeautifulSoup

with open("debug_dom.html", "r", encoding="utf-8") as f:
    soup = BeautifulSoup(f.read(), 'html.parser')

target = soup.select_one('.swiper-slide-active')
if not target:
    print("NO swiper-slide-active")
    exit(1)

content_div = target if 'practice_slide_content' in target.get('class', []) else target.select_one('.practice_slide_content')

correct_labels = []
for i, li in enumerate(target.select('.option_content li, .options li')):
    auto_label = chr(65 + i)
    print(f"Checking option {auto_label}:")
    is_right = li.select_one('input[data-isright="1"]')
    print(f"  is_right={is_right}")
    if is_right:
        correct_labels.append(auto_label)

print(f"correct_labels: {correct_labels}")
