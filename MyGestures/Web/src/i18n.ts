import i18next from "i18next";
import { ref } from "vue";
import en from "../i18n/locales/en-US.json";
import zh from "../i18n/locales/zh-CN.json";
import fr from "../i18n/locales/fr-FR.json";

export const locale = ref("en-US");
export const localeRevision = ref(0);
void i18next.init({ lng: locale.value, fallbackLng: "en-US", initImmediate: false, resources: {
    "en-US": { translation: en }, "zh-CN": { translation: zh }, "fr-FR": { translation: fr },
}, interpolation: { escapeValue: false }, keySeparator: false });
export async function setLocale(value: string): Promise<void> {
    await i18next.changeLanguage(value);
    locale.value = value;
    document.documentElement.lang = value;
    localeRevision.value++;
}
export function t(key: string, defaultValue: string, values: Record<string, unknown> = {}): string {
    void localeRevision.value;
    return i18next.t(key, { defaultValue, ...values });
}
export function escapeHtml(text: string): string {
    return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}
export function highlight(text: string, query: string): string {
    if (!query) return escapeHtml(text);
    const index = text.toLowerCase().indexOf(query.toLowerCase());
    if (index < 0) return escapeHtml(text);
    return escapeHtml(text.slice(0, index)) + "<mark>" + escapeHtml(text.slice(index, index + query.length)) + "</mark>" + highlight(text.slice(index + query.length), query);
}
