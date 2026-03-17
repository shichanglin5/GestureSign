# Training Diagnostics Replay Cases

Place replay case files in this directory.

Each case uses two files:

1. A `.case.json` metadata file.
2. A diagnostics `.log` file exported by GestureSign training diagnostics.

Minimal `.case.json` example:

```json
{
  "name": "three-finger-lrlr",
  "diagnosticsFile": "three-finger-lrlr.log",
  "expectedSequence": [
    "TipTap:Left",
    "TipTap:Right",
    "TipTap:Left",
    "TipTap:Right"
  ]
}
```

`diagnosticsFile` can be either:

- A path relative to this `.case.json` file.
- An absolute path to a diagnostics file.

Supported expected values currently include:

- `TipTap:Left`
- `TipTap:Right`
- `TipTap:Middle`
- `Tap:<fingerCount>`
- `Trajectory`
- `None`

The replay runner scans all `*.case.json` files under this directory.
