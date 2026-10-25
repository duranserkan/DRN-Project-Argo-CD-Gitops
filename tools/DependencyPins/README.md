# Dependency pin updater

`dependencies.json` is the authoritative input for mapped deployment pins. This .NET 10 tool updates their committed consumers without generating whole manifests or changing unrelated configuration. It has no third-party package dependencies and uses the repository's `global.json` SDK.

From the repository root, after verifying candidate tags and digests against their publishers:

```sh
dotnet run --project tools/DependencyPins -- update
git diff --check
git diff
```

Review and commit the inventory and generated changes together, then push. The tool does not stage, commit, publish or deploy. It does not discover newer releases or verify remote digests. Run only when local .NET execution is authorized under the repository rules.

Read-only verification:

```sh
dotnet run --project tools/DependencyPins -- check
```

An optional second argument selects the repository root. Exit codes are `0` for current pins or a successful update, `1` for drift in check mode, and `2` for invalid input or an operational error. CI builds the tool through the [DependencyPins.Tests](../../tests/DependencyPins.Tests/README.md) project reference and invokes check mode before running the tests. Dependency tests do not require Helm.

## Mapping rules

Schema version 2 retains component `pins`, manifest `lines` and external `references`. A manifest may add a `selectors` array parallel to `lines`. A null selector uses the default anchored scalar-key selector, which must match exactly once. Repeated keys require explicit contextual selectors, such as a named container or Helm parameter. Each selector must have exactly one capture group containing the value to update. Do not put versions in selector anchors.

The updater expands `{pinName}` placeholders, resolves all targets and checks for missing, ambiguous or overlapping captures before writing. It rejects unused or unknown pins, duplicate ownership, paths outside the repository and symlink targets. Pin values must be single version/image/digest tokens. Surrounding indentation, quotes, comments, newline style and UTF-8 BOM are preserved.

All mappings are evaluated against the original file content. Changes are applied from the last capture backward so earlier offsets remain valid. Immediately before writing, all changed files are checked for intervening edits. Validation failures write nothing. File writes are not a cross-file filesystem transaction: an I/O failure can leave some files updated. Review the diff, resolve the filesystem problem and rerun update. Do not run concurrent writers against the same files.

The separate dependency-discovery tests still detect supported pin forms missing from the inventory. When adding a new YAML representation, extend discovery and its tests as well as the mapping. Provenance descriptions, compatibility guidance and chart archive verification comments remain manually reviewed documentation.
