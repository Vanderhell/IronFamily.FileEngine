#!/bin/bash
# Verify that the last 50 commits do not have "Claude" in Author or Committer
# Exit 0 if clean, exit 2 if violation found

found_violation=false

git log --format="%an|%cn" -50 | while IFS='|' read -r author committer; do
    if [[ "$author" == *"Claude"* ]]; then
        echo "VIOLATION: Author contains 'Claude': $author" >&2
        found_violation=true
    fi

    if [[ "$committer" == *"Claude"* ]]; then
        echo "VIOLATION: Committer contains 'Claude': $committer" >&2
        found_violation=true
    fi
done

# Check if violations were found
if git log --format="%an|%cn" -50 | grep -i "claude" > /dev/null 2>&1; then
    echo "Git author policy check FAILED. Author/Committer must not contain 'Claude'." >&2
    exit 2
else
    echo "Git author policy check PASSED. All 50 commits have valid authors."
    exit 0
fi
