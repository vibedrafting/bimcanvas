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
import type { FacingData, Module, Point2D } from '../../types/canvas';
import { moduleLibraryService } from '../ModuleLibraryService';
import { LayerManager } from '../three/LayerManager';
import { canvasStyleService } from '../canvas/CanvasStyleService';
import { facingToAngle } from '../../utils/coordinates';

type SvgViewportSource = 'viewBox' | 'geometry';

interface SvgViewport {
  minX: number;
  minY: number;
  width: number;
  height: number;
  source: SvgViewportSource;
}

export class SVGModuleRenderer {
  private scene: THREE.Scene;
  private svgLoader: SVGLoader;
  private svgCache: Map<string, THREE.Group> = new Map();
  private svgSizeCache: Map<string, { width: number, height: number }> = new Map(); // SVG viewport 尺寸缓存
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
        // console.warn(`[SVG] Module not found: ${module.moduleId}`); // 降级日志噪音
        return null;
      }

      // 2. 加载或获取缓存的SVG
      const svgGroup = await this.loadSVG(module.moduleId);
      if (!svgGroup) {
        console.warn(`[SVGModuleRenderer] Failed to load SVG for: ${module.moduleId}`);
        return null;
      }

      // 3. 计算模块的位置和旋转（使用 SVG viewport 尺寸）
      const transform = this.calculateModuleTransform(module, module.moduleId);

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

      // 6. 设置图层（SVG 预览独立图层）
      root.traverse((child) => {
        if (child instanceof THREE.Mesh || child instanceof THREE.Line) {
          child.layers.set(LayerManager.LAYER_SVG);  // 使用 set() 确保只在 LAYER_SVG 上渲染
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
    const svgUrl = await moduleLibraryService.getSvgUrl(moduleId);
    if (!svgUrl) {
      return null;
    }

    return new Promise((resolve) => {
      this.svgLoader.load(
        svgUrl,
        (data) => {
          const paths = data.paths;
          const group = new THREE.Group();
          const svgStyle = canvasStyleService.currentStyle.value.layers.svg;

          // 遍历SVG路径并创建几何体
          for (let i = 0; i < paths.length; i++) {
            const path = paths[i];
            if (!path) continue;

            // 获取填充颜色
            const fillColor = path.userData?.style?.fill;
            if (fillColor && fillColor !== 'none') {
              const shapes = SVGLoader.createShapes(path);

              for (let j = 0; j < shapes.length; j++) {
                const shape = shapes[j];
                if (!shape) continue;
                const geometry = new THREE.ShapeGeometry(shape);
                // 如果是黑色填充，替换为白色（在深色背景下可见）
                const displayFillColor = (fillColor === '#000000' || fillColor === '#000' || fillColor === 'black')
                  ? svgStyle.replaceBlackFill
                  : fillColor;
                const material = new THREE.MeshBasicMaterial({
                  color: new THREE.Color(displayFillColor),
                  side: THREE.DoubleSide,
                  depthTest: svgStyle.depthTest  // 确保不被遮挡
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
                strokeWidth: path.userData?.style?.strokeWidth || svgStyle.defaultStrokeWidth
              };

              // 默认白色描边（SVG CSS class 样式无法被 SVGLoader 解析时的兜底）
              const displayColor = (!strokeColor || strokeColor === '#000000' || strokeColor === '#000' || strokeColor === 'black')
                ? svgStyle.replaceBlackStroke
                : strokeColor;

              const material = new THREE.MeshBasicMaterial({
                color: new THREE.Color(displayColor),
                side: THREE.DoubleSide,
                depthTest: svgStyle.depthTest  // 确保不被遮挡
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

          // 计算 SVG 几何体边界，仅作为日志与无 viewBox 时的降级
          const geometryBox = new THREE.Box3().setFromObject(group);
          const geometrySize = geometryBox.isEmpty()
            ? new THREE.Vector3(0, 0, 0)
            : geometryBox.getSize(new THREE.Vector3());
          const viewport = this.resolveSvgViewport(data.xml, geometryBox);

          // 保存 SVG viewport 尺寸（viewBox 优先），用于映射到模块 bounds
          this.svgSizeCache.set(moduleId, {
            width: viewport.width,
            height: viewport.height
          });

          // 以 viewBox 中心为本地原点，保留 SVG 内部留白和偏移
          const viewportCenterX = viewport.minX + viewport.width / 2;
          const viewportCenterY = viewport.minY + viewport.height / 2;
          group.children.forEach(child => {
            child.position.x -= viewportCenterX;
            child.position.y -= viewportCenterY;
          });

          console.log(`[SVG] ${moduleId}: viewport=${viewport.source}(${viewport.width.toFixed(0)}, ${viewport.height.toFixed(0)}), geometryBounds=(${geometrySize.x.toFixed(0)}, ${geometrySize.y.toFixed(0)}), children=${group.children.length}`);

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
   * @param module 模块数据
   * @param moduleId 模块类型ID（用于查找缓存的 SVG 尺寸）
   */
  private calculateModuleTransform(module: Module, moduleId: string): {
    position: { x: number, y: number };
    rotation: number;
    scale: { x: number, y: number };
  } {
    // 1. 计算模块中心点
    const center = this.calculatePolygonCenter(module.bounds);

    // 2. 解析朝向角度
    const rotation = this.parseFacingAngle(module.facing, module.id);

    // 3. 获取 SVG viewport 尺寸（关键修复：使用 SVG viewBox 尺寸而非几何包围盒）
    const svgSize = this.svgSizeCache.get(moduleId);
    if (!svgSize || svgSize.width <= 0 || svgSize.height <= 0) {
      console.warn(`[SVG] No cached size for: ${moduleId}`);
      return {
        position: { x: center[0], y: center[1] },
        rotation: rotation,
        scale: { x: 1, y: 1 }
      };
    }

    // 4. 按 SVG 旋转后的本地轴匹配 bounds 边长，避免 90° 模块宽深互换
    const targetSize = this.calculateSvgAxisTargetSize(module.bounds, rotation);

    // 5. SVG 在本地坐标系缩放后再旋转，因此缩放目标必须对应旋转后的 SVG X/Y 轴
    const rotationDegrees = Math.abs(rotation * 180 / Math.PI) % 360;
    const scaleX = targetSize.svgX / svgSize.width;
    const scaleY = targetSize.svgY / svgSize.height;

    console.log(`[SVG Scale] target=(${targetSize.svgX.toFixed(0)}, ${targetSize.svgY.toFixed(0)}), svg=(${svgSize.width.toFixed(0)}, ${svgSize.height.toFixed(0)}), rot=${rotationDegrees.toFixed(0)}°, scale=(${scaleX.toFixed(2)}, ${scaleY.toFixed(2)})`);

    return {
      position: { x: center[0], y: center[1] },
      rotation: rotation,
      scale: { x: scaleX * this.SVG_SCALE, y: scaleY * this.SVG_SCALE }
    };
  }

  /**
   * 优先使用根 viewBox 作为 SVG 设计画布；缺失时才降级到实际几何边界。
   */
  private resolveSvgViewport(svgXml: unknown, geometryBox: THREE.Box3): SvgViewport {
    const viewBox = this.parseSvgViewBox(svgXml);
    if (viewBox) {
      return {
        ...viewBox,
        source: 'viewBox'
      };
    }

    if (!geometryBox.isEmpty()) {
      const geometrySize = geometryBox.getSize(new THREE.Vector3());
      return {
        minX: geometryBox.min.x,
        minY: geometryBox.min.y,
        width: geometrySize.x,
        height: geometrySize.y,
        source: 'geometry'
      };
    }

    return {
      minX: 0,
      minY: 0,
      width: 1,
      height: 1,
      source: 'geometry'
    };
  }

  private parseSvgViewBox(svgXml: unknown): Omit<SvgViewport, 'source'> | null {
    const svgElement = this.getSvgRootElement(svgXml);
    const viewBox = svgElement?.getAttribute('viewBox')?.trim();
    if (!viewBox) return null;

    const values = viewBox
      .split(/[,\s]+/)
      .filter(Boolean)
      .map(value => Number(value));

    if (values.length !== 4 || values.some(value => !Number.isFinite(value))) {
      console.warn(`[SVG] Invalid viewBox ignored: ${viewBox}`);
      return null;
    }

    const [minX, minY, width, height] = values as [number, number, number, number];
    if (width <= 0 || height <= 0) {
      console.warn(`[SVG] Non-positive viewBox ignored: ${viewBox}`);
      return null;
    }

    return { minX, minY, width, height };
  }

  private getSvgRootElement(svgXml: unknown): Element | null {
    if (svgXml instanceof Element) {
      return svgXml;
    }
    if (svgXml instanceof Document) {
      return svgXml.documentElement;
    }
    return null;
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
  private parseFacingAngle(facing: FacingData, moduleId?: string): number {
    return -facingToAngle(facing, moduleId ? `SVGModuleRenderer:${moduleId}` : 'SVGModuleRenderer');
  }

  /**
   * 计算 SVG 本地 X/Y 轴应映射到的模块边长。
   * bounds 点序只代表多边形边，不可靠地代表模块宽/深；宽深由 facing 旋转后的轴向决定。
   */
  private calculateSvgAxisTargetSize(polygon: Point2D[], rotation: number): { svgX: number, svgY: number } {
    if (polygon.length < 3) return { svgX: 0, svgY: 0 };

    const edge = (a: Point2D, b: Point2D) => {
      const x = b[0] - a[0];
      const y = b[1] - a[1];
      const length = Math.hypot(x, y);
      return {
        x,
        y,
        length,
        unitX: length > 0 ? x / length : 0,
        unitY: length > 0 ? y / length : 0
      };
    };

    const edge0 = edge(polygon[0]!, polygon[1]!);
    const edge1 = edge(polygon[1]!, polygon[2]!);

    if (edge0.length <= 0 || edge1.length <= 0) {
      return { svgX: edge0.length, svgY: edge1.length };
    }

    const svgXAxis = {
      x: Math.cos(rotation),
      y: Math.sin(rotation)
    };
    const svgYAxis = {
      x: -Math.sin(rotation),
      y: Math.cos(rotation)
    };

    const edge0MatchesSvgX = Math.abs(svgXAxis.x * edge0.unitX + svgXAxis.y * edge0.unitY);
    const edge1MatchesSvgX = Math.abs(svgXAxis.x * edge1.unitX + svgXAxis.y * edge1.unitY);
    const edge0MatchesSvgY = Math.abs(svgYAxis.x * edge0.unitX + svgYAxis.y * edge0.unitY);
    const edge1MatchesSvgY = Math.abs(svgYAxis.x * edge1.unitX + svgYAxis.y * edge1.unitY);

    return {
      svgX: edge0MatchesSvgX >= edge1MatchesSvgX ? edge0.length : edge1.length,
      svgY: edge0MatchesSvgY >= edge1MatchesSvgY ? edge0.length : edge1.length
    };
  }

  /**
   * 更新模块SVG的位置和旋转（用于拖拽时跟随）
   */
  updateModuleTransform(moduleId: string, module: Module): void {
    const root = this.moduleGroups.get(moduleId);
    if (!root) return;

    const svg2D = root.userData.svg2D as THREE.Group;
    if (!svg2D) return;

    const transform = this.calculateModuleTransform(module, module.moduleId);

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
    this.moduleGroups.forEach((_, moduleId) => {
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
    this.svgSizeCache.clear(); // 同步清理尺寸缓存
  }
}
