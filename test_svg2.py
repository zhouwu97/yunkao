import fitz

svg = '<svg width="2.5em" height="1em" xmlns="http://www.w3.org/2000/svg"><text>f(t)</text></svg>'
doc = fitz.open('svg', svg.encode('utf-8'))
print(doc[0].rect)

svg2 = '<svg width="2.5%" height="1%" xmlns="http://www.w3.org/2000/svg"><text>f(t)</text></svg>'
doc2 = fitz.open('svg', svg2.encode('utf-8'))
print(doc2[0].rect)
