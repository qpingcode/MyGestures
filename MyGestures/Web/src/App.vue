<script setup lang="ts">
import { computed, onMounted } from "vue";
import { darkTheme, enUS, zhCN, frFR } from "naive-ui";
import GesturesPanel from "./panels/GesturesPanel.vue";
import { locale, setLocale, t } from "./i18n";
import { store, loadSettings, markGesturesDirty, saveSettings } from "./store";
const localeOptions = [{ label: "English", value: "en-US" }, { label: "简体中文", value: "zh-CN" }, { label: "Français", value: "fr-FR" }];
const uiLocale = computed(() => locale.value === "zh-CN" ? zhCN : locale.value === "fr-FR" ? frFR : enUS);
async function changeLanguage(value: string) { await setLocale(value); store.error = ""; markGesturesDirty(); }
onMounted(loadSettings);
</script>
<template>
    <n-config-provider :theme="darkTheme" :locale="uiLocale">
        <main>
            <header>
                <h1>MyGestures</h1>
                <n-select :value="locale" :options="localeOptions" :aria-label="t('Gestures.Web.Language', 'Language')" @update:value="changeLanguage" />
                <n-button :disabled="!store.dirty || store.capturing || store.loading" :loading="store.saving" @click="saveSettings">{{ t("Gestures.Web.Save", "Save") }}</n-button>
            </header>
            <p>{{ t("Gestures.Web.Description", "Hold the right mouse button and draw, then release to run the assigned action. Closing this window keeps gestures running in the system tray.") }}</p>
            <div v-if="store.error" role="alert" class="error">{{ store.error }}</div>
            <n-spin v-if="store.loading" />
            <GesturesPanel v-else />
        </main>
    </n-config-provider>
</template>
<style>
body { margin: 0; background: #202020; color: #eee; font-family: "Segoe UI Variable Text", "Segoe UI", sans-serif; }
main { padding: 24px; --mt-text: #eee; --mt-surface: #292929; --mt-border: #404040; }
header { display: flex; align-items: center; gap: 16px; }
h1 { flex: 1; margin: 0; font-size: 24px; }
header .n-select { width: 140px; }
p { color: #aaa; line-height: 1.6; }
.error { padding: 12px; color: #ff8888; }
mark { color: inherit; background: #705e20; }
</style>
