const fs = require('fs');
const path = require('path');

const demoPath = String.raw`C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1_20251226_151319\computed`;
const filesToFix = ['room_zones.json', 'exclusions.json'];

function toCamelCase(str) {
    return str.charAt(0).toLowerCase() + str.slice(1);
}

function transformObject(obj) {
    if (Array.isArray(obj)) {
        return obj.map(transformObject);
    } else if (obj !== null && typeof obj === 'object') {
        const newObj = {};
        for (const key in obj) {
            let newKey = toCamelCase(key);
            let value = obj[key];

            // Fix ID values
            if (newKey === 'id' && typeof value === 'string') {
                // User wants "rz_xx" format for zones
                // If it starts with "Room_", convert to "rz_"
                if (value.startsWith('Room_')) {
                    // Extract number: Room_1 -> 1
                    const num = value.substring(5);
                    // Pad with zero if needed: 1 -> 01
                    const paddedNum = num.length < 2 ? '0' + num : num;
                    value = 'rz_' + paddedNum;
                } else if (value.startsWith('r_')) {
                    // If already r_1, convert to rz_01
                    const num = value.substring(2);
                    const paddedNum = num.length < 2 ? '0' + num : num;
                    value = 'rz_' + paddedNum;
                }
            }

            // Fix Boundary fields
            // If we have 'Boundary' or 'boundary', ensure 'computedBoundary' exists
            if (newKey === 'boundary' || newKey === 'Boundary') {
                // If computedBoundary doesn't exist in the parent, we should add it.
                // But we are inside the recursion. 
                // Better strategy: After transforming the object, check for boundary fields.
            }

            // Recursively transform
            newObj[newKey] = transformObject(value);
        }

        // Post-processing for Zone objects
        if (newObj.boundary && !newObj.computedBoundary) {
            newObj.computedBoundary = newObj.boundary;
        }
        // If rawBoundary is missing but boundary exists
        if (newObj.boundary && !newObj.rawBoundary) {
            newObj.rawBoundary = newObj.boundary;
        }

        return newObj;
    }
    return obj;
}

filesToFix.forEach(file => {
    const filePath = path.join(demoPath, file);
    if (fs.existsSync(filePath)) {
        console.log(`Processing ${file}...`);
        try {
            const content = fs.readFileSync(filePath, 'utf8');
            const json = JSON.parse(content);
            const fixedJson = transformObject(json);
            fs.writeFileSync(filePath, JSON.stringify(fixedJson, null, 2));
            console.log(`Fixed ${file}`);
        } catch (e) {
            console.error(`Error processing ${file}:`, e);
        }
    } else {
        console.log(`File not found: ${filePath}`);
    }
});
