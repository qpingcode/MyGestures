<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { darkTheme, enUS, zhCN, frFR } from "naive-ui";
import GeneralPanel from "./panels/GeneralPanel.vue";
import GesturesPanel from "./panels/GesturesPanel.vue";
import { locale, setLocale, t } from "./i18n";
import { store, loadSettings, markGesturesDirty } from "./store";
import { isDarkTheme } from "./theme";
import { useUpdateEvents } from "./update";

const TabGeneral = "general";
const TabGestures = "gestures";
const tab = ref(TabGeneral);
useUpdateEvents(() => { tab.value = TabGeneral; });
const localeOptions = [{ label: "English", value: "en-US" }, { label: "简体中文", value: "zh-CN" }, { label: "Français", value: "fr-FR" }];
const uiLocale = computed(() => locale.value === "zh-CN" ? zhCN : locale.value === "fr-FR" ? frFR : enUS);
const naiveTheme = computed(() => isDarkTheme(store.theme) ? darkTheme : null);
const themeOverrides = computed(() => ({
    common: {
        primaryColor: isDarkTheme(store.theme) ? "#C4A574" : "#8A6A3C",
        primaryColorHover: isDarkTheme(store.theme) ? "#D4B88A" : "#9A7A4C",
        primaryColorPressed: isDarkTheme(store.theme) ? "#B08E5C" : "#6F5530",
        borderRadius: "10px",
        fontFamily: '"Segoe UI Variable Text", "Segoe UI", sans-serif',
    },
}));
const tabs = computed(() => [
    { id: TabGeneral, label: t("Gestures.Web.TabGeneral", "General") },
    { id: TabGestures, label: t("Gestures.Web.TabGestures", "Gestures") },
]);
async function changeLanguage(value: string) { await setLocale(value); store.error = ""; markGesturesDirty(); }
onMounted(loadSettings);
</script>
<template>
    <n-config-provider :theme="naiveTheme" :theme-overrides="themeOverrides" :locale="uiLocale">
        <main :class="{ 'gestures-layout': tab === TabGestures }">
            <nav class="tabs" role="tablist">
                <button
                    v-for="item in tabs"
                    :key="item.id"
                    type="button"
                    class="tab"
                    :class="{ active: tab === item.id }"
                    :data-tab="item.id"
                    role="tab"
                    :aria-selected="tab === item.id"
                    @click="tab = item.id"
                >{{ item.label }}</button>
            </nav>
            <div v-if="store.error" role="alert" class="error">{{ store.error }}</div>
            <n-spin v-if="store.loading" />
            <GeneralPanel v-else-if="tab === TabGeneral">
                <template #language="{ options }">
                    <n-select
                        :value="locale"
                        :options="options ?? localeOptions"
                        :aria-label="t('Gestures.Web.Language', 'Language')"
                        @update:value="changeLanguage"
                    />
                </template>
            </GeneralPanel>
            <GesturesPanel v-else />
        </main>
    </n-config-provider>
</template>
<style>
html, body, #app { min-height: 100%; }
html { color-scheme: dark; }
html[data-theme="light"] { color-scheme: light; }
html[data-theme="dark"] {
    --mt-bg: #141414;
    --mt-text: #f3f1ec;
    --mt-text-secondary: #b3aea4;
    --mt-text-tertiary: #8a857c;
    --mt-surface: #1d1d1d;
    --mt-surface-hover: #2a2a2a;
    --mt-border: #2c2c2c;
    --mt-chip: #111;
    --mt-shadow: rgba(0, 0, 0, 0.28);
    --mt-accent: #c4a574;
    --mt-danger: #ef4444;
}
html[data-theme="light"] {
    --mt-bg: #f6f3ee;
    --mt-text: #1c1b19;
    --mt-text-secondary: #6f6a62;
    --mt-text-tertiary: #8f8a82;
    --mt-surface: #fffdf9;
    --mt-surface-hover: #efeae2;
    --mt-border: #e4ddd2;
    --mt-chip: #ece6dc;
    --mt-shadow: rgba(70, 54, 28, 0.08);
    --mt-accent: #8a6a3c;
    --mt-danger: #c24141;
}
body { margin: 0; background: var(--mt-bg, #141414); color: var(--mt-text, #f3f1ec); font-family: "Segoe UI Variable Text", "Segoe UI", sans-serif; }
main { box-sizing: border-box; min-height: 100vh; padding: 28px 32px 40px; }
main.gestures-layout { height: 100vh; display: flex; flex-direction: column; overflow: hidden; }
main.gestures-layout > .tabs, main.gestures-layout > .error { flex-shrink: 0; }
.tabs { display: flex; gap: 6px; width: fit-content; margin-bottom: 22px; padding: 4px; border-radius: 12px; background: var(--mt-chip, #111); }
.tab { appearance: none; border: none; background: transparent; color: var(--mt-text-secondary, #b3aea4); padding: 8px 16px; border-radius: 9px; font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; }
.tab.active { background: var(--mt-surface, #1d1d1d); color: var(--mt-text, #f3f1ec); box-shadow: 0 1px 4px var(--mt-shadow, rgba(0,0,0,.28)); }
.error { margin-bottom: 16px; padding: 12px 14px; border-radius: 10px; color: #ffb4b4; background: rgba(239, 68, 68, 0.12); }
html[data-theme="light"] .error { color: #9b1c1c; }
mark { color: inherit; background: #705e20; }
html[data-theme="light"] mark { background: #e8d394; }
[data-setting="language"] .n-select { width: 168px; }
</style>
