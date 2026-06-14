import fitz

svg = '<svg width="10ex" height="2ex" xmlns="http://www.w3.org/2000/svg"><text>Hello</text></svg>'
doc = fitz.open('svg', svg.encode('utf-8'))
print(doc[0].rect)

svg2 = '<svg width="100pt" height="20pt" xmlns="http://www.w3.org/2000/svg"><text>Hello</text></svg>'
doc2 = fitz.open('svg', svg2.encode('utf-8'))
print(doc2[0].rect)
