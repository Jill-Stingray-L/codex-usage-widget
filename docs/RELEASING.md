# Releasing

GitHub Releases are created automatically from version tags. The project version
in `src/CodexUsageWidget/CodexUsageWidget.csproj` is the release version source of
truth.

## Publish a version

1. Update `<Version>` in `CodexUsageWidget.csproj`.
2. Commit and push the change to `master`.
3. Wait for the CI workflow to pass.
4. Create and push an annotated tag matching the project version:

   ```powershell
   git tag -a v1.1.0 -m "Release v1.1.0"
   git push origin v1.1.0
   ```

The release workflow validates that the tag and project version match, runs the
test suite, creates the self-contained Windows x64 package, generates its SHA-256
checksum, and publishes both files on GitHub Releases.

Release tags identify immutable published versions and must not be moved or
reused. If a release needs a correction, publish a new patch version instead.
