/**
 * SVG模块渲染器
 * 负责加载SVG文件并将其渲染到Three.js场景中的家具模块位置
 */

import * as THREE from 'three';
import { SVGLoader } from 'three/examples/jsm/loaders/SVGLoader.js';
import type { Module, Point2D } from '../../types/canvas';
import { moduleLibraryService, type ModuleDefinition } from '../ModuleLibraryService';
import { LayerManager } from '../three/LayerManager';

export class SVGModuleRenderer {
  private scene: THREE.Scene;
  private svgLoader: SVGLoader;
  private svgCache: Map<string, THREE.Group> = new Map();
  private moduleGroups: Map<string, THREE.Group> = new Map(); // moduleId -> Group

  // SVG渲染配置
  private readonly SVG_HEIGHT = 760; // SVG图形在3D空间中的高度（高于家具模块的750，显示在家具上方）
  private readonly SVG_SCALE = 1.0; // SVG缩放比例

  constructor(scene: THREE.Scene) {
    this.scene = scene;
    this.svgLoader = new SVGLoader();
  }

  /**
   * 为模块创建SVG渲染
   */
  async renderModuleSVG(module: Module): Promise<THREE.Group | null> {
    try {
      // 1. 从模块库获取模块定义
      const moduleDef = moduleLibraryService.getModuleById(module.moduleId);
      if (!moduleDef) {
        console.warn(`[SVG] Module not found: ${module.moduleId}`);
        return null;
      }

      // 2. 加载或获取缓存的SVG
      const svgUrl = moduleLibraryService.getSvgUrl(module.moduleId);

      const svgGroup = await this.loadSVG(module.moduleId);
      if (!svgGroup) {
        console.warn(`[SVGModuleRenderer] Failed to load SVG for: ${module.moduleId}`);
        return null;
      }

      // 3. 克隆SVG组（因为每个模块实例需要独立的变换）
      const moduleGroup = svgGroup.clone(true);

      // 4. 计算模块的位置和旋转
      const transform = this.calculateModuleTransform(module, moduleDef);

      // 5. 应用变换（转换到 Y-Up 坐标系，与家具模块一致）
      moduleGroup.rotation.x = -Math.PI / 2;
      moduleGroup.position.set(transform.position.x, this.SVG_HEIGHT, -transform.position.y);
      moduleGroup.rotation.y = transform.rotation;
      moduleGroup.scale.set(transform.scale.x, transform.scale.y, 1);

      // [DEBUG] 输出最终变换值
      console.log(`[SVG] ${module.id}: pos=(${moduleGroup.position.x.toFixed(0)}, ${moduleGroup.position.y.toFixed(0)}, ${moduleGroup.position.z.toFixed(0)}), scale=(${moduleGroup.scale.x.toFixed(2)}, ${moduleGroup.scale.y.toFixed(2)})`);

      // 6. 设置图层（与家具模块同层）
      moduleGroup.traverse((child) => {
        if (child instanceof THREE.Mesh || child instanceof THREE.Line) {
          child.layers.enable(LayerManager.LAYER_MODEL);
        }
      });

      // 7. 设置用户数据（用于选择和交互）
      moduleGroup.userData = {
        id: module.id,
        moduleId: module.moduleId,
        type: 'module-svg',
        data: module
      };

      // 8. 添加到场景
      this.scene.add(moduleGroup);

      // 9. 记录到映射表
      this.moduleGroups.set(module.id, moduleGroup);
      return moduleGroup;

    } catch (error) {
      console.error(`[SVGModuleRenderer] Error rendering module SVG:`, error);
      return null;
    }
  }

  /**
   * 加载SVG文件（带缓存）
   * @param moduleId 模块ID，用于从后端 API 获取 SVG
   */
  private async loadSVG(moduleId: string): Promise<THREE.Group | null> {
    // 检查缓存
    if (this.svgCache.has(moduleId)) {
      return this.svgCache.get(moduleId)!;
    }

    // 从 ModuleLibraryService 获取 SVG URL
    const svgUrl = moduleLibraryService.getSvgUrl(moduleId);

    return new Promise((resolve) => {
      this.svgLoader.load(
        svgUrl,
        (data) => {
          const paths = data.paths;
          const group = new THREE.Group();

          // 遍历SVG路径并创建几何体
          for (let i = 0; i < paths.length; i++) {
            const path = paths[i];

            // 获取填充颜色
            const fillColor = path.userData?.style?.fill;
            if (fillColor && fillColor !== 'none') {
              const shapes = SVGLoader.createShapes(path);

              for (let j = 0; j < shapes.length; j++) {
                const shape = shapes[j];
                const geometry = new THREE.ShapeGeometry(shape);
                // 如果是黑色填充，替换为白色（在深色背景下可见）
                const displayFillColor = (fillColor === '#000000' || fillColor === '#000' || fillColor === 'black')
                  ? '#ffffff'
                  : fillColor;
                const material = new THREE.MeshBasicMaterial({
                  color: new THREE.Color(displayFillColor),
                  side: THREE.DoubleSide
                });

                const mesh = new THREE.Mesh(geometry, material);
                group.add(mesh);
              }
            }

            // 获取描边（使用 SVGLoader.pointsToStroke 创建有宽度的描边几何体）
            // 注意：THREE.Line 的 linewidth 在大多数平台被忽略，所以必须用 pointsToStroke
            const strokeColor = path.userData?.style?.stroke;

            // 如果没有填充或填充为 none，且没有明确禁止描边，则渲染描边
            const shouldRenderStroke = strokeColor !== 'none' && (!fillColor || fillColor === 'none' || strokeColor);

            if (shouldRenderStroke) {
              // 构建描边样式（CSS class 未被解析时使用默认值）
              const strokeStyle = {
                ...path.userData?.style,
                strokeWidth: path.userData?.style?.strokeWidth || 20
              };

              // 默认白色描边（SVG CSS class 样式无法被 SVGLoader 解析时的兜底）
              const displayColor = (!strokeColor || strokeColor === '#000000' || strokeColor === '#000' || strokeColor === 'black')
                ? '#ffffff'
                : strokeColor;

              const material = new THREE.MeshBasicMaterial({
                color: new THREE.Color(displayColor),
                side: THREE.DoubleSide
              });

              // 为每个子路径创建描边几何体
              for (const subPath of path.subPaths) {
                const points = subPath.getPoints();
                const strokeGeometry = SVGLoader.pointsToStroke(points, strokeStyle);
                if (strokeGeometry) {
                  const vertexCount = strokeGeometry.attributes.position?.count || 0;
                  // [DEBUG] 关键日志：顶点数
                  console.log(`[SVG] Path${i}: pts=${points.length}, verts=${vertexCount}`);
                  if (vertexCount > 0) {
                    const strokeMesh = new THREE.Mesh(strokeGeometry, material);
                    group.add(strokeMesh);
                  }
                } else {
                  console.warn(`[SVG] Path${i}: pointsToStroke=null, pts=${points.length}`);
                }
              }
            } else if (path.subPaths.length === 0) {
              console.warn(`[SVG] Path${i}: subPaths=0`);
            }
          }

          // 居中 SVG 几何体（将原点从左上角移到几何体中心）
          const box = new THREE.Box3().setFromObject(group);
          const center = box.getCenter(new THREE.Vector3());
          group.children.forEach(child => {
            child.position.x -= center.x;
            child.position.y -= center.y;
          });
          // [DEBUG] 关键日志
          console.log(`[SVG] children=${group.children.length}, center=(${center.x.toFixed(0)}, ${center.y.toFixed(0)})`);

          // 缓存结果
          this.svgCache.set(moduleId, group);
          resolve(group);
        },
        undefined,
        (error) => {
          console.error(`[SVGModuleRenderer] Failed to load SVG: ${moduleId}`, error);
          resolve(null);
        }
      );
    });
  }

  /**
   * 计算模块的变换（位置、旋转、缩放）
   */
  private calculateModuleTransform(module: Module, moduleDef: ModuleDefinition): {
    position: { x: number, y: number };
    rotation: number;
    scale: { x: number, y: number };
  } {
    // 1. 计算模块中心点
    const center = this.calculatePolygonCenter(module.bounds);

    // 2. 解析朝向角度
    const rotation = this.parseFacingAngle(module.facing);

    // 3. 计算缩放（根据bounds和moduleDef.size）
    const boundsSize = this.calculateBoundsSize(module.bounds);
    const scaleX = boundsSize.width / moduleDef.size.width;
    const scaleY = boundsSize.depth / moduleDef.size.depth;

    return {
      position: { x: center[0], y: center[1] },
      rotation: rotation,
      scale: { x: scaleX * this.SVG_SCALE, y: scaleY * this.SVG_SCALE }
    };
  }

  /**
   * 计算多边形中心点
   */
  private calculatePolygonCenter(polygon: Point2D[]): Point2D {
    let cx = 0, cy = 0;
    polygon.forEach(p => {
      cx += p[0];
      cy += p[1];
    });
    return [cx / polygon.length, cy / polygon.length];
  }

  /**
   * 解析朝向为角度（弧度）
   */
  private parseFacingAngle(facing: string | Point2D): number {
    if (typeof facing === 'string') {
      // 语义方向转角度
      const directionMap: { [key: string]: number } = {
        'north': 0,
        'northeast': 45,
        'east': 90,
        'southeast': 135,
        'south': 180,
        'southwest': 225,
        'west': 270,
        'northwest': 315
      };
      const degrees = directionMap[facing.toLowerCase()] || 0;
      return -degrees * Math.PI / 180; // 负号因为Three.js的旋转方向
    } else if (Array.isArray(facing) && facing.length >= 2) {
      // 向量转角度
      return -Math.atan2(facing[0], facing[1]);
    }
    return 0;
  }

  /**
   * 计算多边形边界框尺寸
   */
  private calculateBoundsSize(polygon: Point2D[]): { width: number, depth: number } {
    if (polygon.length === 0) return { width: 0, depth: 0 };

    let minX = Infinity, maxX = -Infinity;
    let minY = Infinity, maxY = -Infinity;

    polygon.forEach(p => {
      minX = Math.min(minX, p[0]);
      maxX = Math.max(maxX, p[0]);
      minY = Math.min(minY, p[1]);
      maxY = Math.max(maxY, p[1]);
    });

    return {
      width: maxX - minX,
      depth: maxY - minY
    };
  }

  /**
   * 更新模块SVG的位置和旋转（用于拖拽时跟随）
   */
  updateModuleTransform(moduleId: string, module: Module): void {
    const group = this.moduleGroups.get(moduleId);
    if (!group) return;

    const moduleDef = moduleLibraryService.getModuleById(module.moduleId);
    if (!moduleDef) return;

    const transform = this.calculateModuleTransform(module, moduleDef);
    group.position.set(transform.position.x, this.SVG_HEIGHT, -transform.position.y);
    group.rotation.y = transform.rotation;
    group.scale.set(transform.scale.x, transform.scale.y, 1);
  }

  /**
   * 移除模块SVG
   */
  removeModuleSVG(moduleId: string): void {
    const group = this.moduleGroups.get(moduleId);
    if (group) {
      this.scene.remove(group);
      this.moduleGroups.delete(moduleId);

      // 清理几何体和材质
      group.traverse((child) => {
        if (child instanceof THREE.Mesh) {
          child.geometry?.dispose();
          if (Array.isArray(child.material)) {
            child.material.forEach(m => m.dispose());
          } else {
            child.material?.dispose();
          }
        } else if (child instanceof THREE.Line) {
          child.geometry?.dispose();
          if (Array.isArray(child.material)) {
            child.material.forEach(m => m.dispose());
          } else {
            child.material?.dispose();
          }
        }
      });
    }
  }

  /**
   * 清除所有SVG
   */
  clear(): void {
    this.moduleGroups.forEach((group, moduleId) => {
      this.removeModuleSVG(moduleId);
    });
    this.moduleGroups.clear();
  }

  /**
   * 清除SVG缓存
   */
  clearCache(): void {
    this.svgCache.forEach((group) => {
      group.traverse((child) => {
        if (child instanceof THREE.Mesh) {
          child.geometry?.dispose();
          if (Array.isArray(child.material)) {
            child.material.forEach(m => m.dispose());
          } else {
            child.material?.dispose();
          }
        }
      });
    });
    this.svgCache.clear();
  }
}
