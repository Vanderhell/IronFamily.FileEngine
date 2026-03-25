# Docs Truth Gate

**Purpose**: Enforce 100% verifiability of normative claims in documentation.

## Overview

The Docs Truth Gate is a CI/CD quality gate that ensures all **normative statements** (MUST, SHOULD, NEVER, ALWAYS, REQUIRED, SHALL) in documentation have valid evidence references.

This is NOT about verifying every informative sentence â€” only normative claims that prescribe behavior or format.

## How It Works

### Normative Keywords
The gate identifies lines containing these keywords at word boundaries:
- `MUST` â€” Mandatory requirement
- `SHOULD` â€” Recommended but optional
- `MUST NOT` â€” Forbidden
- `SHALL` â€” Formal requirement
- `REQUIRED` â€” Required feature
- `NEVER` â€” Explicit prohibition
- `ALWAYS` â€” Unconditional requirement

### Valid Evidence References
A normative claim is considered verified if the line contains:
- **Test framework names**: `IlogTests`, `IronCfgTests`, `IupdTests`, `SpecLockTests`, `PhaseTests`, `RuntimeVerify`, `EdgeError`
- **Test vectors**: `vectors`, `test_vectors`
- **Code references**: File paths containing `.cs`, `.md`, or `Tests`
- **Inline code**: Content wrapped in backticks
- **Implicit evidence**: References like "see SPEC_FILE.md", "verify by", "example"

### Exit Codes
- **0** = All normative claims verified âś“
- **1** = Found unverified normative claims âś—

## Usage

```bash
./tools/docs_truth_gate/verify_docs_truth.sh [docs_directory]
```

Example:
```bash
./tools/docs_truth_gate/verify_docs_truth.sh docs/
```

## CI Integration

The gate is automatically enforced in GitHub Actions:

1. **Build and Test Job**: Runs `dotnet test`
2. **Docs Truth Gate Job**: Runs verification script
3. **Summary Job**: Reports overall CI status

All jobs must pass for CI to succeed.

## Philosophy

**"100% Truth" in Practice**

This is NOT:
- Requiring every sentence to have evidence
- Proof that every statement is correct
- Preventing architectural or design documentation
- Forcing verbose evidence annotations everywhere

This IS:
- Ensuring normative (prescriptive) claims are verifiable
- Preventing unsubstantiated requirements from creeping into docs
- Maintaining alignment between specification and implementation
- A quality gate for enterprise-grade documentation

### Example: What Needs Evidence, What Doesn't

âś“ **HAS EVIDENCE** (normative + evidence):
> "The ILOG file header MUST be exactly 16 bytes (SpecLockTests)"

âś— **NO EVIDENCE** (normative, no evidence):
> "The ILOG file header MUST be exactly 16 bytes"

âś“ **NO EVIDENCE NEEDED** (informative, no evidence required):
> "ILOG (Interactive Log Archive) is a binary format for event logging"

## Maintenance

When adding new normative claims to documentation:

1. Include evidence reference in the same line or nearby paragraph
2. Reference test names, test vector directories, or code files
3. Run `verify_docs_truth.sh` locally before committing
4. CI gate will verify on pull request

## Troubleshooting

### False Positives
If a word like "MUST" appears in a quoted or descriptive context (e.g., describing another document), it may be flagged as a normative claim. Refine the context to avoid the keyword or rephrase.

### Adding New Evidence
To add a new test framework or evidence source:
1. Update the evidence pattern in `verify_docs_truth.sh`
2. Add the keyword to the regex pattern
3. Test locally with `verify_docs_truth.sh`
