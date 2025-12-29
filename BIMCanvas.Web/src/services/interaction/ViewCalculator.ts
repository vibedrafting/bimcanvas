

export class ViewCalculator {
    /**
     * Calculate the target grid spacing and offset to match the final 3D view.
     * @param data Project data (JSON)
     * @param containerWidth Screen width in pixels
     * @param containerHeight Screen height in pixels
     * @returns { spacing: number, offsetX: number, offsetY: number } or null if no data
     */
    public static calculateTargetView(data: any, containerWidth: number, containerHeight: number) {
        // 1. Get Bounds
        const walls = data.baseline?.walls;
        if (!walls || walls.length === 0) return null;

        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;

        const processPolygon = (polygon: any[]) => {
            if (!polygon) return;
            polygon.forEach((p: any) => {
                minX = Math.min(minX, p[0]);
                minY = Math.min(minY, p[1]);
                maxX = Math.max(maxX, p[0]);
                maxY = Math.max(maxY, p[1]);
            });
        };

        if (walls) {
            walls.forEach((wall: any) => processPolygon(wall.polygon));
        }

        if (data.activeScheme?.modules) {
            data.activeScheme.modules.forEach((mod: any) => processPolygon(mod.bounds));
        }

        if (minX === Infinity) return null;

        const centerX = (minX + maxX) / 2;
        const centerY = (minY + maxY) / 2;
        const width = maxX - minX;
        const height = maxY - minY;

        // 2. Calculate Frustum Size (World Units)
        const aspect = containerWidth / containerHeight;
        const padding = 1.5;
        const sizeForHeight = height * padding;
        const sizeForWidth = (width * padding) / aspect;
        const frustumSize = Math.max(sizeForHeight, sizeForWidth);

        // 3. Calculate Dynamic Island Offset
        const topUIHeight = 120; // px
        const offsetPixels = topUIHeight / 2;
        const offsetWorld = offsetPixels * (frustumSize / containerHeight);

        // 4. Calculate Center 3D
        // The camera looks at (centerX, 0, -centerY - offsetWorld)
        // World (0,0,0) is at (-centerX, 0, centerY + offsetWorld) relative to camera center
        const targetCenterX = centerX;
        const targetCenterY = -centerY - offsetWorld; // Map 2D Y (Up) to World Z (Down/Negative)

        // 5. Calculate Scale (Pixels per World Unit)
        // FrustumHeight = frustumSize
        // ScreenHeight = containerHeight
        const pixelsPerUnit = containerHeight / frustumSize;

        // 6. Calculate Grid Spacing in Pixels
        // World Grid is 1000mm
        const worldGridSpacing = 1000;
        const gridSpacingPixels = worldGridSpacing * pixelsPerUnit;

        // 7. Calculate Grid Origin Offset in Pixels
        // The camera center is at screen center (W/2, H/2).
        // The camera center in World Coords is (targetCenterX, targetCenterY).
        // We want the position of World Origin (0,0) on Screen.

        // Delta from Camera Center to World Origin:
        // dX = 0 - targetCenterX
        // dY = 0 - targetCenterY

        // Screen Delta (World Z maps directly to Screen Y, so no inversion needed for delta)
        // World Z increases (Down) -> Screen Y increases (Down)

        const screenDeltaX = (0 - targetCenterX) * pixelsPerUnit;
        const screenDeltaY = (0 - targetCenterY) * pixelsPerUnit;

        const screenOriginX = containerWidth / 2 + screenDeltaX;
        const screenOriginY = containerHeight / 2 + screenDeltaY;

        return {
            spacing: gridSpacingPixels,
            offsetX: screenOriginX, // This is the screen coordinate of the grid origin (0,0)
            offsetY: screenOriginY
        };
    }
}
