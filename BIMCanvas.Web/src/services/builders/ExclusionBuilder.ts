import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';
import type { ProjectData, Zone, Point2D } from '../../types/canvas';
import { canvasStyleService } from '../canvas/CanvasStyleService';

export class ExclusionBuilder {
    private scene: THREE.Scene;
    private exclusionGroup: THREE.Group | null = null;
    private materials: Map<string, THREE.Material>;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.materials = new Map();
        this.initMaterials();
    }

    private initMaterials() {
        const colors = canvasStyleService.currentStyle.value.layers.zones;

        // 门扇禁区材质 - 红色半透明
        this.materials.set('door_swing', new THREE.MeshBasicMaterial({
            color: colors.exclusionDoorSwing.color,
            transparent: true,
            opacity: colors.exclusionDoorSwing.opacity,
            side: THREE.DoubleSide,
            depthWrite: false
        }));

        // 通用禁区材质
        this.materials.set('default', new THREE.MeshBasicMaterial({
            color: colors.exclusion.color,
            transparent: true,
            opacity: colors.exclusion.opacity,
            side: THREE.DoubleSide,
            depthWrite: false
        }));
    }

    private ensureMaterials() {
        if (this.materials.size === 0) {
            this.initMaterials();
        }
    }

    /**
     * 清理禁区资源
     * @param disposeMaterials 是否释放材质（仅在彻底重建/销毁时使用）
     */
    public cleanup(disposeMaterials = false) {
        if (this.exclusionGroup) {
            this.exclusionGroup.traverse(child => {
                if ((child as THREE.Mesh).geometry) {
                    (child as THREE.Mesh).geometry.dispose();
                }
            });
            this.exclusionGroup.clear();
            this.scene.remove(this.exclusionGroup);
            this.exclusionGroup = null;
        }
        if (disposeMaterials) {
            this.materials.forEach(material => material.dispose());
            this.materials.clear();
        }
    }

    public buildExclusions(data: ProjectData) {
        this.ensureMaterials();

        // 清理旧禁区
        if (this.exclusionGroup) {
            this.exclusionGroup.traverse(child => {
                if ((child as THREE.Mesh).geometry) {
                    (child as THREE.Mesh).geometry.dispose();
                }
            });
            this.exclusionGroup.clear();
            this.scene.remove(this.exclusionGroup);
            this.exclusionGroup = null;
        }

        this.exclusionGroup = new THREE.Group();
        this.exclusionGroup.layers.set(LayerManager.LAYER_ZONES);

        // 从 computed.exclusions 获取禁区数据
        const exclusions = data.computed?.exclusions;
        if (exclusions && exclusions.length > 0) {
            exclusions.forEach(exclusion => {
                if (!exclusion.visible) return;
                this.createExclusionMesh(exclusion);
            });
        }

        this.scene.add(this.exclusionGroup);
    }

    private createExclusionMesh(exclusion: Zone) {
        const boundary = exclusion.rawBoundary;
        if (!boundary || boundary.length < 3) return;

        const shape = this.createShapeFromPolygon(boundary);
        const geometry = new THREE.ShapeGeometry(shape);

        const subType = this.parseSubType(exclusion.reason);
        const materialKey = this.materials.has(subType) ? subType : 'default';
        let material = this.materials.get(materialKey);
        if (!material) {
            this.ensureMaterials();
            material = this.materials.get(materialKey) ?? this.materials.get('default');
        }
        if (!material) {
            return;
        }

        const mesh = new THREE.Mesh(geometry, material);
        mesh.rotation.x = -Math.PI / 2;
        mesh.position.y = 12;  // 高于 Zone (10)
        mesh.layers.set(LayerManager.LAYER_ZONES);

        const { subType: parsedSubType, description } = this.parseReason(exclusion.reason);
        mesh.userData = {
            id: exclusion.id,
            type: 'exclusion',
            subType: parsedSubType,
            reason: description
        };

        this.exclusionGroup!.add(mesh);
    }

    /**
     * 解析 reason 字段，提取子类型和描述
     * 格式: {subType}:{description}
     */
    private parseReason(reason: string): { subType: string; description: string } {
        const colonIndex = reason.indexOf(':');
        if (colonIndex === -1) {
            return { subType: 'unknown', description: reason };
        }
        return {
            subType: reason.substring(0, colonIndex),
            description: reason.substring(colonIndex + 1)
        };
    }

    private parseSubType(reason: string): string {
        const colonIndex = reason.indexOf(':');
        return colonIndex === -1 ? 'unknown' : reason.substring(0, colonIndex);
    }

    private createShapeFromPolygon(polygon: Point2D[]): THREE.Shape {
        const shape = new THREE.Shape();
        if (!polygon || polygon.length === 0) return shape;

        const firstPoint = polygon[0];
        if (firstPoint) {
            shape.moveTo(firstPoint[0], firstPoint[1]);
            for (let i = 1; i < polygon.length; i++) {
                const point = polygon[i];
                if (point) {
                    shape.lineTo(point[0], point[1]);
                }
            }
        }
        shape.closePath();
        return shape;
    }
}
