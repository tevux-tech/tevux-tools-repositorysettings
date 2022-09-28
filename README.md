This NuGet creates following repository-level config files, that are used by essentially all projects, `.editorconfig`, `.gitattributes` and `.gitignore`. Additionally, `Settings.XamlStyler` is included. It is only needed for WPF apps, but does not really hurt anyway.

# Usage caveats

There are several important caveats related to the usage of this NuGet.

1. Setting files are copied next to the solution .sln file; for this reason, install the nuget to the **primary project only** (if there's more than one).
2. Files are copied before each build, so you need to at least try building the project for the settings file to appear.
3. Files are restored on every build, so do not modify them or the changes will be lost immediatelly. If you need to add some very project-specific settings (like, for example, custom ignored file types for Nuke Build System), create local `.editorconfig`, `.gitattributes` or `.gitignore` file. *These files stack up*, so you can override or append settings on per-project basis, if needed.

