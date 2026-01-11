/**
 * SVG模块渲染器
 * 负责加载SVG文件并将其渲染到Three.js场景中的家具模块位置
 *
 * ========== 重要技术警告 ==========
 *
 * Three.js Euler 旋转陷阱：
 * - 默认 XYZ 顺序是绕**本地轴**依次旋转
 * - 在同一对象上设置 rotation.x 后再设置 rotation.y，
 *   rotation.y 是绕已旋转后的本地 Y 轴，不是世界 Y 轴！
 *
 * 错误示例（会导致 SVG 变成垂直面）：
 *   group.rotation.x = -Math.PI / 2;  // 压平
 *   group.rotation.y = facing;        // ❌ 绕本地 Y 轴，会把 SVG 掀起来
 *
 * 正确方案（父子 Group）：
 *   const root = new THREE.Group();   // 父级：只做压平
 *   const svg2D = group.clone(true);  // 子级：在 XY 平面内做 2D 旋转
 *   svg2D.rotation.z = facing;        // ✅ 绕 Z 轴（在 XY 平面内）
 *   root.add(svg2D);
 *   root.rotation.x = -Math.PI / 2;   // 最后压平
 *
 * 验证方法：
 *   Box3.getSize() 的 size.y ≈ 0 表示平面是水平的
 *
 * 参考：reports/SVG_Rendering_Issue_Report.md
 * =====================================
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
  private readonly SVG_HEIGHT = 800; // SVG图形在3D空间中的高度（略高于家具模块顶面750）
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

      // 3. 计算模块的位置和旋转
      const transform = this.calculateModuleTransform(module, moduleDef);

      // 4. 父子 Group 方案（KISS）：显式控制旋转顺序
      // - svg2D（子级）：负责 2D 朝向旋转（绕 Z）+ 2D 缩放（仍在 XY 平面）
      // - root（父级）：负责压平（rotation.x）+ 世界坐标定位
      const root = new THREE.Group();
      const svg2D = svgGroup.clone(true);

      // 子级：2D 变换（在 XY 平面内）
      svg2D.rotation.set(0, 0, transform.rotation);  // 绕 Z 做朝向（不取反）
      svg2D.scale.set(transform.scale.x, transform.scale.y, 1);

      root.add(svg2D);

      // 父级：压平 + 世界坐标定位
      root.rotation.set(-Math.PI / 2, 0, 0);  // 压平（XY → XZ）
      root.position.set(transform.position.x, this.SVG_HEIGHT, -transform.position.y);

      // 5. 设置渲染属性（兜底：确保不被遮挡）
      root.renderOrder = 999;
      svg2D.traverse((child) => {
        if (child instanceof THREE.Mesh) {
          child.renderOrder = 999;
          if (child.material instanceof THREE.MeshBasicMaterial) {
            child.material.depthTest = false;
          }
        }
      });

      // 6. 设置图层（与家具模块同层）
      root.traverse((child) => {
        if (child instanceof THREE.Mesh || child instanceof THREE.Line) {
          child.layers.enable(LayerManager.LAYER_MODEL);
        }
      });

      // 7. 设置用户数据（用于选择和交互）
      root.userData = {
        id: module.id,
        moduleId: module.moduleId,
        type: 'module-svg',
        data: module,
        svg2D: svg2D  // 保存引用，供 updateModuleTransform 使用
      };

      // 8. 添加到场景
      this.scene.add(root);

      // [DEBUG] 验证几何体方向
      const box = new THREE.Box3().setFromObject(root);
      const size = box.getSize(new THREE.Vector3());
      console.log(`[SVG] ${module.id}: pos=(${root.position.x.toFixed(0)}, ${root.position.y.toFixed(0)}, ${root.position.z.toFixed(0)}), size=(${size.x.toFixed(0)}, ${size.y.toFixed(0)}, ${size.z.toFixed(0)})`);

      // 9. 记录到映射表
      this.moduleGroups.set(module.id, root);
      return root;

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
                  side: THREE.DoubleSide,
                  depthTest: false  // 确保不被遮挡
                });

                const mesh = new THREE.Mesh(geometry, material);
                mesh.renderOrder = 999;
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
                side: THREE.DoubleSide,
                depthTest: false  // 确保不被遮挡
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
                    strokeMesh.renderOrder = 999;
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
    const root = this.moduleGroups.get(moduleId);
    if (!root) return;

    const moduleDef = moduleLibraryService.getModuleById(module.moduleId);
    if (!moduleDef) return;

    const svg2D = root.userData.svg2D as THREE.Group;
    if (!svg2D) return;

    const transform = this.calculateModuleTransform(module, moduleDef);

    // 更新子级（svg2D）：2D 变换
    svg2D.rotation.set(0, 0, transform.rotation);
    svg2D.scale.set(transform.scale.x, transform.scale.y, 1);

    // 更新父级（root）：世界坐标定位（压平旋转不变）
    root.position.set(transform.position.x, this.SVG_HEIGHT, -transform.position.y);
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
