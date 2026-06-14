import re
import time

# Old regex that causes catastrophic backtracking
old_regex = r'!\[[^\]]*\]\(((?:[^)(]+|\([^)(]*\))*)\)'

# New regex without catastrophic backtracking
new_regex = r'!\[[^\]]*\]\(([^()]*(?:\([^()]*\)[^()]*)*)\)'

# A long string with a missing closing parenthesis, which causes backtracking catastrophe
bad_string = "![img](data:image/svg+xml;utf8,<svg " + "A" * 1000 + "(" + "B" * 1000

print("Testing new regex...")
start = time.time()
re.finditer(new_regex, bad_string)
# just iterate once to trigger
list(re.finditer(new_regex, bad_string))
print(f"New regex took {time.time() - start:.5f} seconds")

print("Testing old regex...")
start = time.time()
try:
    # We use match instead of full execution to avoid hanging the test forever, but we'll put a timeout or small size
    bad_string_small = "![img](data:image/svg+xml;utf8,<svg " + "A" * 30 + "(" + "B" * 30
    list(re.finditer(old_regex, bad_string_small))
    print(f"Old regex small took {time.time() - start:.5f} seconds")
    
    # This would hang forever:
    # list(re.finditer(old_regex, bad_string))
except Exception as e:
    print(e)
