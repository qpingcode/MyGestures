import { createApp } from "vue";
import { create, NButton, NCheckbox, NConfigProvider, NInput, NSelect, NSpin, NSwitch } from "naive-ui";
import "@mdi/font/css/materialdesignicons.css";
import App from "./App.vue";
const naive = create({ components: [NButton, NCheckbox, NConfigProvider, NInput, NSelect, NSpin, NSwitch] });
createApp(App).use(naive).mount("#app");
