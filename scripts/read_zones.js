const fs = require('fs');
const path = String.raw`C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1_20251226_151319\computed\room_zones.json`;

try {
    if (fs.existsSync(path)) {
        const content = fs.readFileSync(path, 'utf8');
        console.log('--- CONTENT START ---');
        console.log(content);
        console.log('--- CONTENT END ---');
    } else {
        console.log('File does not exist:', path);
    }
} catch (e) {
    console.error('Error reading file:', e);
}
