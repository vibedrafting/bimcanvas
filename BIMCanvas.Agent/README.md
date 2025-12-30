# BIMCanvas Agent

AI-powered interior layout assistant based on Anthropic Agent SDK.

## Quick Start

### 1. Install Dependencies

```bash
cd BIMCanvas.Agent
pip install -e .
```

### 2. Configure Environment

```bash
# Copy example config
cp .env.example .env

# Edit .env and add your Anthropic API key
# ANTHROPIC_API_KEY=your-api-key-here
```

### 3. Run the Agent

**HTTP Server Mode (for Web integration):**

```bash
python -m src.main --serve
```

The server will start at `http://127.0.0.1:8765`

**Interactive CLI Mode:**

```bash
python -m src.main
```

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/api/chat` | POST | Send a chat message |
| `/api/chat/stream` | POST | Send a chat message (SSE stream) |
| `/api/clear-history` | POST | Clear conversation history |
| `/api/history` | GET | Get conversation history |

### Chat Request Example

```json
POST /api/chat
{
  "projectPath": "C:/path/to/project",
  "message": "帮我设计客厅的布置方案"
}
```

### Response

```json
{
  "reply": "AI response here...",
  "projectPath": "C:/path/to/project"
}
```

## Project Structure

```
BIMCanvas.Agent/
├── pyproject.toml          # Project configuration
├── .env.example            # Environment config template
├── README.md               # This file
├── src/
│   ├── __init__.py
│   ├── main.py             # Entry point
│   ├── agent/
│   │   └── placement_agent.py  # Main Agent (Agent SDK)
│   ├── server/
│   │   └── http_server.py      # HTTP server (aiohttp)
│   ├── tools/
│   │   ├── file_tools.py       # JSON read/write
│   │   └── svg_parser.py       # SVG parsing
│   └── config/
│       └── settings.py         # Configuration
├── MOSS/                   # Legacy code (reference only)
└── AgentSDK-Quickstart.md  # Agent SDK documentation
```

## Development

### Current Status: P1 Phase (MVP)

- [x] Agent SDK integration
- [x] HTTP server with CORS
- [x] Basic chat functionality
- [x] Web integration ready
- [ ] Tool calling (P2)
- [ ] Layout generation (P2)

### Next Steps (P2 Phase)

1. Add tool definitions for reading project data
2. Implement layout decision logic
3. Add layout task API endpoint

See `plans/Agent_MVP.md` for the complete implementation plan.
