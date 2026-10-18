Bugs of the original that the C# reproduces on purpose, collected from the `// BUG:` comments.
A bug that has a fix flag in `OriginalBehaviour` (`src/Darklands.Core/OriginalBehaviour.cs`)
can be switched off for a "fixed" game; all flags are off by default (the faithful game). The
flag is found by looking for `Fix...` names near the comment. Bugs without a flag are
reproduced only. Quirks that are documented only in the remarks of `OriginalBehaviour` (no
`// BUG:` comment) are not in this register. `original_bugs.csv` holds the same data.
