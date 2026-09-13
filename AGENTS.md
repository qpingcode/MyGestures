# MyGestures repository instructions

MyGestures is a standalone Windows application. It must not depend on MyTools projects, plugin hosting, Node at runtime, or IPC forwarding through MyTools.

## Build verification

Build MyGestures/Web using npm ci and npm run build before building .NET. Use an independent output directory from the start, such as -p:OutputPath=bin/AgentVerification/, because the running application may lock default outputs. Do not stop the user's application to verify changes.

## Localization

Localize all new or changed UI text in the same change, including validation, notifications, tooltips and accessibility names. Keep localization keys and English fallbacks literal at call sites.

Native UI uses MyGestures/Localization/HostStrings.resx, HostStrings.zh-CN.resx and HostStrings.fr-FR.resx through an injected LocalizationService. Web UI uses MyGestures/Web/i18n JSON catalogs and i18next through src/i18n.ts. Follow package.json.i18n.supportedLocales and update every supported locale. Preserve named placeholders, and recompute visible translated state when the locale changes. Do not mix native and Web resource keys across the bridge.

Stable identifiers, paths, protocol fields and persisted user values are not localizable. Developer diagnostics are exempt unless directly presented to users. Do not hand-edit dist; run the Web build.

## Named domain values

Do not introduce unexplained business literals. Use enums or typed named values for discrete states. Give thresholds, time periods, limits, stable identifier prefixes and protocol method names descriptive constants at their owning scope. Reuse existing definitions. Preserve behavior when extracting constants. Obvious structural values, localization keys/fallbacks, declarative configuration, regex syntax and diagnostic templates may remain literal.

## Checks

Run npm run check and npm run build for Web changes. Run the affected .NET build and relevant tests with the independent output directory. Review all affected locale entries and placeholder sets before completing UI changes.
