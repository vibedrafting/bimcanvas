# AITOLL API 接口对接指南

## 概述

AITOLL 是一个第三方大模型接口聚合服务平台，提供统一的 OpenAI 兼容格式接口，支持多种大模型的调用。

### 核心特性

- **OpenAI 兼容**：采用 OpenAI 标准请求/响应格式，便于迁移和集成
- **多模型支持**：支持文本对话、图像生成与处理等多种模型
- **统一入口**：所有模型通过同一端点调用，仅需切换 `model` 参数

## 快速开始

### 基础配置

| 配置项       | 值                                                    |
| ------------ | ----------------------------------------------------- |
| Base URL     | `https://your-provider-base-url/api/chat/completions` |
| 请求方式     | POST                                                  |
| Content-Type | application/json                                      |

### API Key 配置

建议将 API Key 配置在环境变量中，避免硬编码：

```bash
export AITOLL_API_KEY="your-api-key-here"
```

### 请求头

```
Authorization: Bearer YOUR_API_KEY
Content-Type: application/json
```

## 可用模型

| 模型名称                     | 用途            | 说明                               |
| ---------------------------- | --------------- | ---------------------------------- |
| deepseek-chat                | 文本对话        | 经济的对话模型                     |
| gpt-5.2                      | 文本对话        | 行业优秀的对话模型                 |
| claude-haiku-4.5             | 文本对话        | 经济的对话模型、代码能力优秀       |
| claude-sonnet-4.5            | 文本对话        | 对话模型                           |
| gemin-3-flash-preview        | 文本对话/多模态 | 经济的多模态模型                   |
| gemini-3-pro-preview         | 文本对话/多模态 | 多模态模型                         |
| `gemini-3-pro-image-preview` | 图像生成/编辑   | 图像专用模型、支持文生图、图像编辑 |

> 💡 具体可用模型列表请参考 AITOLL 平台文档或控制台

---

## API 调用说明

### 请求格式

```json
{
  "model": "模型名称",
  "messages": [
    {
      "role": "user" | "assistant" | "system",
      "content": "文本内容" | [多模态内容数组]
    }
  ],
  "stream": false
}
```

### 消息内容格式

**纯文本格式**（适用于普通对话）：

```json
{
  "role": "user",
  "content": "你好，请介绍一下你自己"
}
```

**多模态格式**（适用于图像处理）：

```json
{
  "role": "user",
  "content": [
    {
      "type": "text",
      "text": "请描述这张图片"
    },
    {
      "type": "image_url",
      "image_url": {
        "url": "图片URL或Base64数据"
      }
    }
  ]
}
```

### 图片输入支持格式

| 格式        | 示例                                 |
| ----------- | ------------------------------------ |
| URL 链接    | `https://example.com/image.jpg`      |
| Base64 编码 | `data:image/jpeg;base64,/9j/4AAQ...` |

---

## 使用示例

### 1. 普通文本对话

**请求**：

```json
{
  "model": "your-chat-model",
  "messages": [
    {
      "role": "system",
      "content": "你是一个有帮助的助手。"
    },
    {
      "role": "user",
      "content": "请简单介绍一下人工智能的发展历史"
    }
  ]
}
```

**响应**：

```json
{
  "id": "chatcmpl-xxx",
  "object": "chat.completion",
  "created": 1766997975,
  "model": "your-chat-model",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "人工智能（AI）的发展可以追溯到20世纪50年代..."
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 25,
    "completion_tokens": 150,
    "total_tokens": 175
  }
}
```

### 2. 文本生成图像

**请求**：

```json
{
  "model": "gemini-3-pro-image-preview",
  "messages": [
    {
      "role": "user",
      "content": [
        {
          "type": "text",
          "text": "生成一个苹果公司的logo"
        }
      ]
    }
  ]
}
```

**响应**：

```json
{
  "id": "chatcmpl-gemini-mjqwucuu-58ene7",
  "object": "chat.completion",
  "created": 1766997759,
  "model": "gemini-3-pro-image-preview",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": [
          {
            "type": "image_url",
            "image_url": {
              "url": "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQEBLAEsA..."
            }
          }
        ]
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 6,
    "completion_tokens": 1190,
    "total_tokens": 1323
  }
}
```

### 3. 图像编辑

**请求**：

```json
{
  "model": "gemini-3-pro-image-preview",
  "messages": [
    {
      "role": "user",
      "content": [
        {
          "type": "text",
          "text": "将这张图背景改为蓝色"
        },
        {
          "type": "image_url",
          "image_url": {
            "url": "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQEBLAEsAA..."
          }
        }
      ]
    }
  ]
}
```

**响应**：返回编辑后的图像（Base64 格式）

### 4. 图像理解/描述

**请求**：

```json
{
  "model": "gemini-3-pro-image-preview",
  "messages": [
    {
      "role": "user",
      "content": [
        {
          "type": "text",
          "text": "请描述这张图片的内容"
        },
        {
          "type": "image_url",
          "image_url": {
            "url": "https://example.com/sample.jpg"
          }
        }
      ]
    }
  ]
}
```

**响应**：

```json
{
  "id": "chatcmpl-gemini-mjqwyzs7-5qz44k",
  "object": "chat.completion",
  "created": 1766997975,
  "model": "gemini-3-pro-image-preview",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "这张图片展示了苹果公司的标志。一个银色的、被咬了一口的苹果图案位于画面中央..."
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 262,
    "completion_tokens": 81,
    "total_tokens": 442
  }
}
```

---

## 响应格式说明

### 响应字段

| 字段      | 类型   | 说明                               |
| --------- | ------ | ---------------------------------- |
| `id`      | string | 请求唯一标识                       |
| `object`  | string | 对象类型，固定为 `chat.completion` |
| `created` | number | 创建时间戳                         |
| `model`   | string | 使用的模型名称                     |
| `choices` | array  | 响应结果数组                       |
| `usage`   | object | Token 使用统计                     |

### Content 返回格式

根据模型和请求内容，`content` 可能返回以下格式：

**纯文本**：
```json
"content": "这是文本回复内容"
```

**图像（Base64）**：
```json
"content": [
  {
    "type": "image_url",
    "image_url": {
      "url": "data:image/jpeg;base64,..."
    }
  }
]
```

---

## 代码示例

### Python

```python
import os
import requests
import base64

API_KEY = os.environ.get("AITOLL_API_KEY")
BASE_URL = "https://your-provider-base-url/api/chat/completions"

headers = {
    "Authorization": f"Bearer {API_KEY}",
    "Content-Type": "application/json"
}

# 文本对话
def chat(prompt: str, model: str = "your-chat-model"):
    payload = {
        "model": model,
        "messages": [{"role": "user", "content": prompt}]
    }
    response = requests.post(BASE_URL, headers=headers, json=payload)
    return response.json()["choices"][0]["message"]["content"]

# 图像生成
def generate_image(prompt: str):
    payload = {
        "model": "gemini-3-pro-image-preview",
        "messages": [{
            "role": "user",
            "content": [{"type": "text", "text": prompt}]
        }]
    }
    response = requests.post(BASE_URL, headers=headers, json=payload)
    return response.json()["choices"][0]["message"]["content"]

# 图像编辑
def edit_image(prompt: str, image_path: str):
    with open(image_path, "rb") as f:
        image_base64 = base64.b64encode(f.read()).decode()
  
    payload = {
        "model": "gemini-3-pro-image-preview",
        "messages": [{
            "role": "user",
            "content": [
                {"type": "text", "text": prompt},
                {"type": "image_url", "image_url": {"url": f"data:image/jpeg;base64,{image_base64}"}}
            ]
        }]
    }
    response = requests.post(BASE_URL, headers=headers, json=payload)
    return response.json()["choices"][0]["message"]["content"]
```

### cURL

```bash
curl -X POST "https://your-provider-base-url/api/chat/completions" \
  -H "Authorization: Bearer $AITOLL_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "your-chat-model",
    "messages": [{"role": "user", "content": "你好"}]
  }'
```

---

## 注意事项

1. **API Key 安全**：请勿在客户端代码中硬编码 API Key
2. **图片大小限制**：上传图片时注意文件大小限制（具体限制请参考平台说明）
3. **请求频率**：请遵守平台的请求频率限制
4. **错误处理**：建议对 API 调用进行异常捕获和重试机制

---

## 常见问题

**Q: 如何判断返回的是图片还是文本？**

A: 检查 `content` 字段的类型。如果是字符串则为文本；如果是数组且包含 `type: "image_url"` 的对象则为图片。

**Q: 支持流式输出吗？**

A: 请参考平台文档确认具体模型的流式输出支持情况。
