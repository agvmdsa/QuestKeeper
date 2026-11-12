# Build from source and releases

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (the exact version is in `global.json`).

```sh
dotnet build AqwCompanion.slnx -c Release   # build everything
dotnet test AqwCompanion.slnx -c Release    # unit tests, the architecture tests and the headless UI tests
dotnet run --project src/AqwCompanion.App   # run the app (the capture needs Windows, Npcap and an elevated terminal)
```

The phone page lives in `web/` and the exe embeds the built page, so build it before the .NET build: `cd web && npm ci && npm run build`. Without `web/dist` the build stops with a message; pass `-p:SkipWebCheck=true` to skip that check (the exe then serves nothing on the phone port).

A `vX.Y.Z` tag runs `.github/workflows/release.yml`: tests, a self-contained single-file `win-x64` publish, and a zip, with `SHA256SUMS.txt` for every file. The Skua plugin DLL is attached too (not inside the zip or the installer). The version comes from the tag (MinVer).
