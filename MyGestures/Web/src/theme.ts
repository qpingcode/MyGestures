export const Theme = { Light: "light", Dark: "dark" } as const;
export type ThemeName = typeof Theme[keyof typeof Theme];

export function resolveTheme(theme: string): ThemeName {
    return theme === Theme.Light ? Theme.Light : Theme.Dark;
}

export function applyTheme(theme: string): void {
    document.documentElement.dataset.theme = resolveTheme(theme);
}

export function isDarkTheme(theme: string): boolean {
    return resolveTheme(theme) === Theme.Dark;
}
