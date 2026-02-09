# 在 Claude Agent SDK 中创建截图 MCP 工具

本教程将教你如何创建一个截图 MCP 工具。**重要提示**：虽然 MCP 协议理论上支持返回图片，但官方文档没有提供在 Agent SDK 中返回图片的验证示例。因此，本教程提供三种方案供选择。

## ⚠️ 重要声明

1. **MCP 协议支持图片** - 官方规范包含 image 类型
2. **Agent SDK 类型定义包含图片** - TypeScript 类型中有 `type: 'image'`
3. **但官方无示例** - 所有官方 Custom Tools 示例都只返回文本
4. **需要自行验证** - 建议先测试小图片，确认你的环境支持

## 方案一：保存文件法（最稳妥，推荐生产环境）

这种方法将截图保存为文件，让 Claude 使用内置的 Read 工具查看。**优点**：肯定能工作，无大小限制。

### 步骤 1：创建工具文件

创建 `screenshot-file.js`：

```javascript
import { createSdkMcpServer, tool } from '@anthropic-ai/agent-sdk';
import { z } from 'zod';
import screenshot from 'screenshot-desktop';
import fs from 'fs/promises';
import path from 'path';

// 定义截图工具
const screenshotTool = tool(
  'take_screenshot',
  '截取屏幕截图并保存为文件，返回文件路径',
  {
    filename: z.string().optional().describe('保存的文件名，默认为 screenshot.png'),
    display: z.number().optional().describe('显示器编号，默认为主显示器'),
  },
  async (args) => {
    try {
      // 确保 screenshots 目录存在
      const screenshotsDir = './screenshots';
      await fs.mkdir(screenshotsDir, { recursive: true });
      
      // 截取屏幕
      const imgBuffer = await screenshot({ screen: args.display });
      
      // 确定文件路径
      const filename = args.filename || `screenshot-${Date.now()}.png`;
      const filePath = path.join(screenshotsDir, filename);
      
      // 保存文件
      await fs.writeFile(filePath, imgBuffer);
      
      // 获取文件大小
      const stats = await fs.stat(filePath);
      const sizeKB = (stats.size / 1024).toFixed(2);
      
      // 返回成功消息和路径
      return {
        content: [{
          type: 'text',
          text: `截图成功保存到：${filePath}\n文件大小：${sizeKB}KB\n\n请使用 Read 工具查看图片内容。`
        }]
      };
      
    } catch (error) {
      return {
        content: [{
          type: 'text',
          text: `截图失败: ${error.message}`
        }],
        isError: true
      };
    }
  }
);

// 创建 SDK MCP Server
export const screenshotServer = createSdkMcpServer({
  name: 'screenshot',
  version: '1.0.0',
  tools: [screenshotTool]
});
```

### 步骤 2：在 Agent SDK 中使用

创建 `app.js`：

```javascript
import { query } from '@anthropic-ai/agent-sdk';
import { screenshotServer } from './screenshot-file.js';

async function main() {
  const result = query({
    prompt: '请截取屏幕，然后读取并描述图片内容',
    options: {
      mcpServers: {
        'screenshot': screenshotServer
      },
      allowedTools: [
        'mcp__screenshot__take_screenshot',
        'Read'  // 允许 Claude 读取保存的图片
      ],
      permissionMode: 'bypass_permissions'
    }
  });

  for await (const message of result) {
    if (message.type === 'text') {
      console.log(message.text);
    }
  }
}

main();
```

### 步骤 3：测试

```bash
npm install @anthropic-ai/agent-sdk screenshot-desktop zod
node app.js
```

**工作流程**：
1. Claude 调用 `take_screenshot` 工具
2. 工具保存截图到 `./screenshots/` 目录
3. 工具返回文件路径给 Claude
4. Claude 自动使用 `Read` 工具读取图片
5. Claude 分析并描述图片内容

---

## 方案二：直接返回图片法（实验性）

这种方法尝试直接返回 base64 编码的图片。**警告**：官方文档没有示例验证，需要自行测试。

### 最小化测试

在实现完整截图功能前，先测试 Agent SDK 是否真的支持返回图片：

```javascript
import { createSdkMcpServer, tool, query } from '@anthropic-ai/agent-sdk';
import { z } from 'zod';

// 一个 1x1 红色像素的 PNG（base64，只有 68 字节）
const TINY_RED_PIXEL = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==";

const testServer = createSdkMcpServer({
  name: 'test',
  tools: [
    tool('test_image', '测试返回一个很小的图片', {}, async () => {
      return {
        content: [{
          type: 'image',
          data: TINY_RED_PIXEL,
          mimeType: 'image/png'
        }]
      };
    })
  ]
});

// 测试
for await (const msg of query({
  prompt: '请使用 test_image 工具并告诉我你看到了什么颜色',
  options: {
    mcpServers: { test: testServer },
    allowedTools: ['mcp__test__test_image']
  }
})) {
  console.log(msg);
}
```

**如果这个测试成功**（Claude 能看到红色像素），那么你可以继续实现完整的截图功能。

### 完整实现（仅在测试成功后使用）

创建 `screenshot-image.js`：

```javascript
import { createSdkMcpServer, tool } from '@anthropic-ai/agent-sdk';
import { z } from 'zod';
import screenshot from 'screenshot-desktop';
import sharp from 'sharp';

const screenshotTool = tool(
  'take_screenshot',
  '截取屏幕截图并返回图片',
  {
    width: z.number().optional().describe('目标宽度，默认800'),
    height: z.number().optional().describe('目标高度，默认600'),
  },
  async (args) => {
    try {
      // 截取屏幕
      let imgBuffer = await screenshot();
      
      // 激进压缩
      imgBuffer = await sharp(imgBuffer)
        .resize(args.width || 800, args.height || 600, { 
          fit: 'inside',
          withoutEnlargement: true
        })
        .png({ 
          quality: 60,
          compressionLevel: 9 
        })
        .toBuffer();
      
      const base64Image = imgBuffer.toString('base64');
      const sizeKB = Buffer.byteLength(base64Image, 'base64') / 1024;
      
      // 安全检查
      if (sizeKB > 512) {
        return {
          content: [{
            type: 'text',
            text: `图片压缩后仍然太大（${sizeKB.toFixed()}KB），超过安全限制。请降低分辨率或使用方案一（文件保存法）。`
          }],
          isError: true
        };
      }
      
      // 尝试返回图片
      return {
        content: [
          {
            type: 'text',
            text: `截图成功，图片大小：${sizeKB.toFixed()}KB`
          },
          {
            type: 'image',
            data: base64Image,
            mimeType: 'image/png'
          }
        ]
      };
      
    } catch (error) {
      return {
        content: [{
          type: 'text',
          text: `截图失败: ${error.message}`
        }],
        isError: true
      };
    }
  }
);

export const screenshotServer = createSdkMcpServer({
  name: 'screenshot',
  version: '1.0.0',
  tools: [screenshotTool]
});
```

---

## 方案三：混合方法（最灵活）

同时保存文件和返回图片，提供双重保障：

```javascript
import { createSdkMcpServer, tool } from '@anthropic-ai/agent-sdk';
import { z } from 'zod';
import screenshot from 'screenshot-desktop';
import sharp from 'sharp';
import fs from 'fs/promises';
import path from 'path';

const screenshotTool = tool(
  'take_screenshot',
  '截取屏幕截图，同时保存文件和返回图片',
  {},
  async (args) => {
    try {
      // 截取并压缩
      let imgBuffer = await screenshot();
      imgBuffer = await sharp(imgBuffer)
        .resize(800, 600, { fit: 'inside' })
        .png({ quality: 70, compressionLevel: 9 })
        .toBuffer();
      
      // 保存文件
      const screenshotsDir = './screenshots';
      await fs.mkdir(screenshotsDir, { recursive: true });
      const filename = `screenshot-${Date.now()}.png`;
      const filePath = path.join(screenshotsDir, filename);
      await fs.writeFile(filePath, imgBuffer);
      
      // 准备返回
      const base64Image = imgBuffer.toString('base64');
      const sizeKB = Buffer.byteLength(base64Image, 'base64') / 1024;
      
      const content = [
        {
          type: 'text',
          text: `截图已保存到：${filePath}\n大小：${sizeKB.toFixed()}KB`
        }
      ];
      
      // 如果不太大，也返回图片
      if (sizeKB <= 512) {
        content.push({
          type: 'image',
          data: base64Image,
          mimeType: 'image/png'
        });
      } else {
        content[0].text += '\n\n图片较大，请使用 Read 工具查看文件。';
      }
      
      return { content };
      
    } catch (error) {
      return {
        content: [{
          type: 'text',
          text: `截图失败: ${error.message}`
        }],
        isError: true
      };
    }
  }
);

export const screenshotServer = createSdkMcpServer({
  name: 'screenshot',
  version: '1.0.0',
  tools: [screenshotTool]
});
```

---

## 推荐使用流程

1. **立即可用**：使用方案一（文件保存法），肯定能工作
2. **先测试**：运行最小化测试，确认你的环境支持返回图片
3. **如果测试成功**：
   - 谨慎尝试方案二或方案三
   - 从小图片开始（< 100KB）
   - 逐步增加到 500KB
4. **如果测试失败**：
   - 继续使用方案一
   - 向 Anthropic 报告问题
   - 等待官方支持或文档更新

---

## 依赖安装

```bash
# 基础依赖
npm install @anthropic-ai/agent-sdk screenshot-desktop zod

# 如果使用图片压缩（方案二、三）
npm install sharp
```

---

## 常见问题

**Q: 为什么官方没有返回图片的示例？**  
A: 目前不清楚。可能是：
- 功能还在测试中
- 存在未记录的限制
- 官方推荐使用文件方式
- 文档还未更新

**Q: 我应该使用哪种方案？**  
A: 
- **生产环境**：方案一（文件保存法），最稳定
- **实验/开发**：先测试，成功后可尝试方案二或三
- **不确定**：方案三（混合法），兼顾稳定性和功能性

**Q: 1MB 限制是硬性的吗？**  
A: 根据社区讨论，Claude Desktop 有 1MB 限制。Agent SDK 的限制可能不同，需要测试。建议保持在 500KB 以下更保险。

**Q: 为什么要压缩图片？**  
A: 
1. base64 编码会使大小增加约 33%
2. 避免超过大小限制
3. 减少 token 消耗
4. 提高响应速度

**Q: Claude 真的能"看到"图片吗？**  
A: 是的，Claude 模型支持视觉输入。但前提是：
1. 图片成功传递给模型
2. 格式正确
3. 大小在限制内

**Q: 如果我的测试失败了怎么办？**  
A: 
1. 使用方案一（文件保存法）
2. 在 GitHub 上报告问题
3. 等待社区或官方解答

---

## 验证清单

在部署前，请确认：

- [ ] 运行最小化测试，确认图片返回能工作
- [ ] 测试不同大小的图片（50KB, 200KB, 500KB）
- [ ] 验证 Claude 能正确描述图片内容
- [ ] 检查性能影响（响应时间、token 消耗）
- [ ] 准备降级方案（如果图片返回失败）
- [ ] 记录实际的大小限制
- [ ] 考虑错误处理和用户提示

---

## 总结

### 关键要点

1. ✅ **MCP 协议支持图片**，这是确定的
2. ⚠️ **Agent SDK 理论上支持**，但缺少官方示例
3. ⚠️ **必须先测试**，不同环境可能表现不同
4. ✅ **文件保存法最稳妥**，推荐生产环境使用
5. ⚠️ **严格控制大小**，建议 < 500KB
6. ✅ **图片压缩是必要的**，使用 sharp 库

### 我的建议

作为一个负责任的开发者：

1. **不要盲目相信**理论上的支持
2. **一定要测试**你的具体环境
3. **准备降级方案**以防万一
4. **从小图片开始**，逐步测试
5. **记录你的发现**，帮助社区

如果你测试成功了，请考虑：
- 分享你的配置和经验
- 在 GitHub 上贡献示例
- 帮助完善文档

---

## 参考资源

- [MCP 官方规范 - Tools](https://modelcontextprotocol.io/specification/2025-06-18/server/tools)
- [Agent SDK 官方文档](https://platform.claude.com/docs/en/agent-sdk/overview)
- [Agent SDK Custom Tools](https://platform.claude.com/docs/en/agent-sdk/custom-tools)
- [GitHub 讨论：返回图片问题](https://github.com/orgs/modelcontextprotocol/discussions/199)
- [screenshot-desktop](https://www.npmjs.com/package/screenshot-desktop)
- [sharp 图片处理](https://sharp.pixelplumbing.com/)

---

## 鸣谢

感谢你指出我原始教程的问题。这促使我更深入地研究了官方文档和实际实践，发现了理论和实践之间的差距。

这个更新的教程基于：
- MCP 官方规范
- Agent SDK 源码类型定义
- 社区讨论和实践
- Cursor 等其他 MCP 客户端的实现

但请记住：**在你自己的环境中验证之前，不要假设任何东西都能工作。**

