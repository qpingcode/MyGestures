<script setup lang="ts">
import { computed } from "vue";
import { t } from "../i18n";
import { store } from "../store";
import { Theme } from "../theme";

const localeOptions = [
    { label: "English", value: "en-US" },
    { label: "简体中文", value: "zh-CN" },
    { label: "Français", value: "fr-FR" },
];
const themeOptions = computed(() => [
    { label: t("Gestures.Web.ThemeLight", "Light"), value: Theme.Light, icon: "mdi-white-balance-sunny" },
    { label: t("Gestures.Web.ThemeDark", "Dark"), value: Theme.Dark, icon: "mdi-moon-waning-crescent" },
]);
</script>

<template>
    <section class="general">
        <article class="setting">
            <div class="copy">
                <h2>{{ t("Gestures.Web.Enable", "Enable gestures") }}</h2>
                <p>{{ t("Gestures.Web.EnableDescription", "Listen for right-button gestures on this computer.") }}</p>
            </div>
            <n-switch
                :aria-label="t('Gestures.Web.Enable', 'Enable gestures')"
                :value="store.enabled"
                @update:value="store.enabled = !!$event"
            />
        </article>
        <article class="setting">
            <div class="copy">
                <h2>{{ t("Gestures.Web.AutoStart", "Start with Windows") }}</h2>
                <p>{{ t("Gestures.Web.AutoStartDescription", "Open MyGestures in the tray when you sign in.") }}</p>
            </div>
            <n-switch
                :aria-label="t('Gestures.Web.AutoStart', 'Start with Windows')"
                :value="store.autoStart"
                @update:value="store.autoStart = !!$event"
            />
        </article>
        <article class="setting" data-setting="language">
            <div class="copy">
                <h2>{{ t("Gestures.Web.Language", "Language") }}</h2>
                <p>{{ t("Gestures.Web.LanguageDescription", "Language for the settings window and gesture trail.") }}</p>
            </div>
            <slot name="language" :options="localeOptions" />
        </article>
        <article class="setting">
            <div class="copy">
                <h2>{{ t("Gestures.Web.Theme", "Theme") }}</h2>
                <p>{{ t("Gestures.Web.ThemeDescription", "Choose a light or dark appearance.") }}</p>
            </div>
            <div class="theme-switch" role="radiogroup" :aria-label="t('Gestures.Web.Theme', 'Theme')">
                <button
                    v-for="option in themeOptions"
                    :key="option.value"
                    type="button"
                    class="theme-option"
                    :class="{ active: store.theme === option.value }"
                    role="radio"
                    :aria-checked="store.theme === option.value"
                    @click="store.theme = option.value"
                >
                    <i class="mdi" :class="option.icon"></i>
                    {{ option.label }}
                </button>
            </div>
        </article>
        <article class="setting">
            <div class="copy">
                <h2>{{ t("Gestures.Web.GameMode", "Game mode") }}</h2>
                <p>{{ t("Gestures.Web.GameModeDescription", "Pause gestures while another app is fullscreen, so games and videos keep the right mouse button.") }}</p>
            </div>
            <n-switch
                :aria-label="t('Gestures.Web.GameMode', 'Game mode')"
                :value="store.gameMode"
                @update:value="store.gameMode = !!$event"
            />
        </article>
    </section>
</template>

<style scoped>
.general {
    display: flex;
    flex-direction: column;
    gap: 10px;
    max-width: 720px;
}

.setting {
    display: flex;
    align-items: center;
    gap: 24px;
    padding: 18px 20px;
    border: 1px solid var(--mt-border);
    border-radius: 14px;
    background: var(--mt-surface);
}

.copy {
    flex: 1;
    min-width: 0;
}

h2 {
    margin: 0;
    font-size: 15px;
    font-weight: 600;
    letter-spacing: 0.01em;
    color: var(--mt-text);
}

p {
    margin: 6px 0 0;
    font-size: 13px;
    line-height: 1.55;
    color: var(--mt-text-secondary);
}

.theme-switch {
    display: flex;
    padding: 3px;
    border-radius: 11px;
    background: var(--mt-chip);
}

.theme-option {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    min-width: 88px;
    padding: 7px 12px;
    border: none;
    border-radius: 8px;
    background: transparent;
    color: var(--mt-text-secondary);
    font: inherit;
    font-size: 13px;
    cursor: pointer;
}

.theme-option.active {
    background: var(--mt-surface);
    color: var(--mt-text);
    box-shadow: 0 1px 4px var(--mt-shadow);
}

.theme-option i {
    font-size: 16px;
}
</style>
