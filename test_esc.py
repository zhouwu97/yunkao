import html, re
text = '① |z| > 0.5 \n ② |z| < 0.25 ![img]<http://url|align:-1ex>'
text = html.escape(text)
print('Escaped:', text)
def rep(m):
    return f'<img src="{html.unescape(m.group(1))}">'
text = re.sub(r'!\[[^\]]*\]&lt;([^&]+)&gt;', rep, text)
print('Replaced:', text)
