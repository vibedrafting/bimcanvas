const fs = require('fs');
const target = 'e:\\\\工作文档\\\\开发类\\\\MyCode\\\\BIMCanvas\\\\BIMCanvas.Web\\\\src\\\\components\\\\UI\\\\HomeSettingsPanel.vue';
const template = 'e:\\\\工作文档\\\\开发类\\\\MyCode\\\\BIMCanvas\\\\BIMCanvas.Web\\\\src\\\\components\\\\UI\\\\HomeSettingsPanel_Template.vue';

try {
  const targetContent = fs.readFileSync(target, 'utf8');
  const templateContent = fs.readFileSync(template, 'utf8');
  const index = targetContent.indexOf('<template>');
  if (index !== -1) {
    const newContent = targetContent.substring(0, index) + templateContent;
    fs.writeFileSync(target, newContent, 'utf8');
    console.log('Merge complete.');
  } else {
    console.log('No <template> tag found.');
  }
} catch (e) {
  console.error('Error:', e);
}
