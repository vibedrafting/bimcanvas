<template>
  <div class="settings-page">
    <div class="settings-shell">
      <header class="page-header">
        <div class="header-main">
          <button class="icon-back-btn" type="button" @click="emit('close')" title="返回项目列表">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M15 18l-6-6 6-6" />
            </svg>
          </button>
          <h2>{{ pageTitle }}</h2>
        </div>
        <div class="header-badges">
          <span class="mode-pill">{{ modeLabel }}</span>
          <span class="status-pill" :class="restartPendingGroups.length > 0 ? 'status-pending' : 'status-ready'">
            <i class="dot"></i>
            {{ restartPendingGroups.length > 0 ? `待重启 ${restartPendingGroups.length} 组配置` : '已同步到当前实例' }}
          </span>
        </div>
      </header>

      <div
        v-if="loadError || saveError || saveMessage || restartPendingGroups.length > 0"
        class="notice-stack container-padding"
      >
        <div v-if="loadError" class="notice error">{{ loadError }}</div>
        <div v-if="saveError" class="notice error">{{ saveError }}</div>
        <div v-if="saveMessage" class="notice info">{{ saveMessage }}</div>
        <div v-if="restartPendingGroups.length > 0" class="notice warn">
          已修改高影响配置：{{ restartPendingGroups.join(' / ') }}。保存后请完成一次实例重启。
          ({{ runtime.restartBehavior === 'docker-auto' ? 'Docker 将自动拉起' : '需要手动重启服务' }})
        </div>
      </div>

      <div v-if="isLoading" class="loading-state container-padding">正在加载实例配置...</div>

      <template v-else>
        <div class="main-settings-card">
          <!-- 运行方式区块 -->
          <section class="config-section">
            <div class="section-title">
              <h3>运行方式</h3>
              <p>决定默认模型由哪一组配置控制，并影响重启后的真实行为。</p>
            </div>
            <div class="section-content">
              <div class="form-row">
                <label class="switch-field outline-box">
                  <div class="switch-meta">
                    <span class="switch-label">启用 CCR (大模型网关)</span>
                    <span class="switch-desc">开启后使用 CCR 作为路由中枢</span>
                  </div>
                  <div class="switch">
                    <input v-model="drafts.server.values.ccr.enabled" type="checkbox">
                    <span class="slider"></span>
                  </div>
                </label>
              </div>
              <div class="form-row col-2">
                <label class="field">
                  <span>Agent 端口</span>
                  <input v-model.number="drafts.server.values.server.port" type="number">
                </label>
                <label class="field">
                  <span>Python 命令</span>
                  <input v-model="drafts.server.values.server.pythonCommand" type="text" placeholder="python 或 python3">
                </label>
              </div>
              <div class="form-row col-2">
                <label class="field">
                  <span>CCR Host</span>
                  <input v-model="drafts.server.values.ccr.host" type="text" :disabled="!isCcrMode">
                </label>
                <label class="field">
                  <span>CCR Port</span>
                  <input v-model.number="drafts.server.values.ccr.port" type="number" :disabled="!isCcrMode">
                </label>
              </div>
            </div>
          </section>

          <div class="section-divider"></div>

          <!-- 默认模型与推理行为区块 -->
          <section class="config-section">
            <div class="section-title">
              <h3>默认模型与推理行为</h3>
              <p>
                {{ effectiveModelDescription }}
                <span class="path-badge">{{ displayEffectiveModelPath }}</span>
              </p>
            </div>
            <div class="section-content">
              <div class="form-row col-2">
                <label class="field">
                  <span>默认模型</span>
                  <select v-model="effectiveDefaultModel">
                    <option v-for="option in modelOptions" :key="option.value" :value="option.value">
                      {{ option.label }}
                    </option>
                  </select>
                </label>
                <label class="field">
                  <span>默认 Effort</span>
                  <select v-model="drafts.agent.values.defaultEffort" :disabled="isCcrMode && !drafts.ccr.values.Router?.default">
                    <option v-for="option in effortOptions" :key="option.value" :value="option.value">
                      {{ option.label }}
                    </option>
                  </select>
                </label>
              </div>
              <div class="form-row col-2">
                <label class="field">
                  <span>默认 Thinking</span>
                  <select v-model="drafts.agent.values.defaultThinking">
                    <option v-for="option in thinkingOptions" :key="option.value" :value="option.value">
                      {{ option.label }}
                    </option>
                  </select>
                </label>
                <label class="field">
                  <span>最大 Thinking Tokens</span>
                  <input v-model.number="drafts.agent.values.maxThinkingTokens" type="number">
                </label>
              </div>

              <div class="sub-section-title">模型系列映射 (Model Mapping)</div>
              <div class="form-row col-3">
                <label class="field">
                  <span>Opus 映射</span>
                  <input
                    :value="modelMappingValue('opus')"
                    type="text"
                    placeholder="claude-opus-4"
                    @input="setModelMappingValue('opus', ($event.target as HTMLInputElement).value)"
                  >
                </label>
                <label class="field">
                  <span>Sonnet 映射</span>
                  <input
                    :value="modelMappingValue('sonnet')"
                    type="text"
                    placeholder="claude-sonnet-4-20250514"
                    @input="setModelMappingValue('sonnet', ($event.target as HTMLInputElement).value)"
                  >
                </label>
                <label class="field">
                  <span>Haiku 映射</span>
                  <input
                    :value="modelMappingValue('haiku')"
                    type="text"
                    placeholder="claude-haiku-4-5-20251001"
                    @input="setModelMappingValue('haiku', ($event.target as HTMLInputElement).value)"
                  >
                </label>
              </div>
            </div>
          </section>

          <div class="section-divider"></div>

          <!-- 连接与鉴权区块 -->
          <section class="config-section">
            <div class="section-title split-title">
              <div class="title-left">
                <h3>连接与参数配额</h3>
                <p>根据当前运行模式展示真正参与运行的连接配置。</p>
              </div>
              <button class="text-button" type="button" @click="showSecrets = !showSecrets">
                {{ showSecrets ? '隐藏密钥' : '显示密钥' }}
              </button>
            </div>
            <div class="section-content">
              <template v-if="!isCcrMode">
                <div class="form-row">
                  <label class="field">
                    <span>请求地址 (Base URL)</span>
                    <input v-model="drafts.agent.values.baseUrl" type="text" placeholder="https://api.anthropic.com">
                  </label>
                </div>
                <div class="form-row">
                  <label class="field">
                    <span>API Key</span>
                    <input
                      v-model="drafts.agent.values.apiKey"
                      :type="showSecrets ? 'text' : 'password'"
                      placeholder="未设置"
                    >
                    <small class="helper-text">{{ showSecrets ? '当前显示明文' : maskSecret(drafts.agent.values.apiKey) }}</small>
                  </label>
                </div>
              </template>

              <template v-else>
                <div class="form-row col-2">
                  <label class="field">
                    <span>本地 CCR Host</span>
                    <input v-model="drafts.ccr.values.HOST" type="text">
                  </label>
                  <label class="field">
                    <span>本地 CCR Port</span>
                    <input v-model.number="drafts.ccr.values.PORT" type="number">
                  </label>
                </div>

                <div class="sub-section-title">Providers 配置</div>
                <div class="providers-list">
                  <article
                    v-for="(provider, index) in drafts.ccr.values.Providers"
                    :key="provider.name || index"
                    class="provider-box"
                  >
                    <div class="provider-header">
                      <div class="provider-name">{{ provider.name || `Provider ${index + 1}` }}</div>
                      <span class="provider-badge">{{ provider.models?.length || 0 }} 个模型</span>
                    </div>
                    
                    <div class="form-row">
                      <label class="field">
                        <span>请求地址</span>
                        <input v-model="provider.api_base_url" type="text">
                      </label>
                    </div>
                    <div class="form-row">
                      <label class="field">
                        <span>API Key</span>
                        <input v-model="provider.api_key" :type="showSecrets ? 'text' : 'password'">
                        <small class="helper-text">{{ showSecrets ? '当前显示明文' : maskSecret(provider.api_key || '') }}</small>
                      </label>
                    </div>
                    <div class="form-row">
                      <label class="field">
                        <span>模型列表 (逗号分隔)</span>
                        <input :value="providerModels(provider)" type="text" @input="updateProviderModels(provider, $event)">
                      </label>
                    </div>
                  </article>
                </div>
                <div v-if="primaryProvider" class="info-strip outline-box highlight">
                  当前主要 Provider：<strong>{{ primaryProvider.name }}</strong>，默认路由：<strong>{{ drafts.ccr.values.Router.default || '未设置' }}</strong>
                </div>
              </template>
            </div>
          </section>

          <!-- 可折叠区块：CCR 路由 (仅在CCR模式) -->
          <details v-if="isCcrMode" class="accordion-section group-divider">
            <summary class="accordion-header">
              <div class="accordion-title">
                <h3>CCR 路由与日志设置</h3>
                <p>展开编辑 CCR 模式下的默认路由和日志行为参数。</p>
              </div>
              <div class="accordion-toggle-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 9l6 6 6-6"/></svg>
              </div>
            </summary>
            <div class="accordion-content">
              <div class="form-row col-2">
                <label class="field">
                  <span>Router: Default</span>
                  <input v-model="drafts.ccr.values.Router.default" type="text">
                </label>
                <label class="field">
                  <span>Router: Think</span>
                  <input v-model="drafts.ccr.values.Router.think" type="text">
                </label>
              </div>
              <div class="form-row col-2">
                <label class="field">
                  <span>Router: Background</span>
                  <input v-model="drafts.ccr.values.Router.background" type="text">
                </label>
                <label class="field">
                  <span>Router: Long Context</span>
                  <input v-model="drafts.ccr.values.Router.longContext" type="text">
                </label>
              </div>
              <div class="form-row col-2">
                <label class="field">
                  <span>API_TIMEOUT_MS</span>
                  <input v-model.number="drafts.ccr.values.API_TIMEOUT_MS" type="number">
                </label>
                <label class="switch-field outline-box mini-switch">
                  <span class="switch-label">启用详细 LOG</span>
                  <div class="switch">
                    <input v-model="drafts.ccr.values.LOG" type="checkbox">
                    <span class="slider"></span>
                  </div>
                </label>
              </div>
            </div>
          </details>

          <!-- 可折叠区块：Web 展示配置 -->
          <details class="accordion-section group-divider" open>
            <summary class="accordion-header">
              <div class="accordion-title">
                <h3>Web 客户端展示配置</h3>
                <p>独立影响前端展示的热更新字段，保存即生效无需重启服务。</p>
              </div>
              <div class="accordion-toggle-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 9l6 6 6-6"/></svg>
              </div>
            </summary>
            <div class="accordion-content">
              <div class="form-row">
                <label class="field">
                  <span>自定义模型列表 (每行一条)</span>
                  <textarea :value="modelLines()" rows="3" @input="handleModelLinesInput" />
                </label>
              </div>
              <div class="form-row col-2">
                <label class="field">
                  <span>User 图层预设 (每行一个预设组)</span>
                  <textarea
                    :value="drafts.web.values.layerPresets.User.enabledLayers.join('\n')"
                    rows="3"
                    @input="handleLayerPresetInput(drafts.web.values.layerPresets.User.enabledLayers, $event)"
                  />
                </label>
                <label class="field">
                  <span>Agent 图层预设 (每行一个预设组)</span>
                  <textarea
                    :value="drafts.web.values.layerPresets.Agent.enabledLayers.join('\n')"
                    rows="3"
                    @input="handleLayerPresetInput(drafts.web.values.layerPresets.Agent.enabledLayers, $event)"
                  />
                </label>
              </div>
            </div>
          </details>

          <!-- 可折叠区块：高级 JSON -->
          <details class="accordion-section group-divider">
            <summary class="accordion-header">
              <div class="accordion-title">
                <h3>高级配置 (JSON Viewer)</h3>
                <p>保留对底层组态的硬编辑能力，如遇到严重字段解析问题可在此修正。</p>
              </div>
              <div class="accordion-toggle-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 9l6 6 6-6"/></svg>
              </div>
            </summary>
            <div class="accordion-content">
              <div class="json-list">
                <div v-for="key in groupKeys" :key="key" class="json-item-wrapper">
                  <div class="json-item-head">
                    <strong>{{ drafts[key].title }}</strong>
                    <span class="path-badge">{{ drafts[key].sourceFile }}</span>
                  </div>
                  <textarea v-model="drafts[key].jsonText" rows="10" spellcheck="false" @blur="parseJson(key)" />
                  <p v-if="drafts[key].jsonError" class="json-error">{{ drafts[key].jsonError }}</p>
                </div>
              </div>
            </div>
          </details>
        </div>
      </template>
    </div>

    <!-- 底部操作栏 -->
    <footer class="sticky-footer">
      <div class="footer-inner">
        <div class="footer-info">
          <span class="info-label">配置文件将直接应用到实例环境</span>
        </div>
        <div class="footer-actions">
          <button class="secondary-button" type="button" :disabled="isLoading" @click="loadSettings">取消并重置</button>
          <button
            v-if="restartPendingGroups.length > 0"
            class="danger-button"
            type="button"
            :disabled="isRestarting"
            @click="handleRestart"
          >
            {{ isRestarting ? '重启中...' : '重启实例让其生效' }}
          </button>
          <button class="primary-button" type="button" :disabled="isSaving || isLoading" @click="handleSave">
            {{ isSaving ? '保存中...' : '保存更改' }}
          </button>
        </div>
      </div>
    </footer>
  </div>
</template>

<style scoped>
.settings-page {
  --page-surface: rgba(17, 20, 31, 0.78);
  --page-surface-strong: rgba(11, 14, 22, 0.92);
  --page-border: rgba(255, 255, 255, 0.08);
  --page-border-strong: rgba(255, 255, 255, 0.16);
  --page-text: var(--text-primary, #ffffff);
  --page-text-secondary: rgba(255, 255, 255, 0.65);
  --page-text-tertiary: rgba(255, 255, 255, 0.45);
  --page-accent: var(--accent-blue, #3b82f6);
  --page-accent-hover: #60a5fa;
  position: relative;
  min-height: 100%;
  padding: 0 0 132px;
  color: var(--page-text);
}

.settings-page::before {
  content: '';
  position: absolute;
  inset: 0;
  pointer-events: none;
  background: 
    radial-gradient(circle at 14% 0%, rgba(59, 130, 246, 0.12), transparent 30%),
    linear-gradient(180deg, rgba(255, 255, 255, 0.02), transparent 40%);
  z-index: 0;
}

.settings-shell {
  position: relative;
  z-index: 1;
  max-width: 960px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 32px 16px 24px;
}

.header-main {
  display: flex;
  align-items: center;
  gap: 16px;
}

.icon-back-btn {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  border: 1px solid var(--page-border-strong);
  background: rgba(255, 255, 255, 0.05);
  color: var(--page-text);
  display: grid;
  place-items: center;
  cursor: pointer;
  transition: all 0.2s ease;
}

.icon-back-btn:hover {
  background: rgba(255, 255, 255, 0.1);
  transform: translateX(-2px);
}

.icon-back-btn svg {
  width: 18px;
  height: 18px;
}

.header-main h2 {
  margin: 0;
  font-size: 1.5rem;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.header-badges {
  display: flex;
  align-items: center;
  gap: 12px;
}

.mode-pill, .status-pill {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 6px 14px;
  border-radius: 999px;
  font-size: 0.85rem;
  font-weight: 500;
  background: rgba(255, 255, 255, 0.04);
  border: 1px solid var(--page-border);
}

.status-ready { color: #86efac; }
.status-ready .dot { background: #86efac; }
.status-pending { color: #fdf08a; background: rgba(253, 224, 71, 0.06); border-color: rgba(253, 224, 71, 0.2); }
.status-pending .dot { background: #fdf08a; box-shadow: 0 0 8px rgba(253, 224, 71, 0.4); animation: pulse 2s infinite; }

.dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
}

@keyframes pulse {
  0% { opacity: 1; }
  50% { opacity: 0.5; }
  100% { opacity: 1; }
}

.container-padding {
  padding: 0 16px;
  margin-bottom: 24px;
}

.main-settings-card {
  margin: 0 16px;
  background: rgba(18, 21, 31, 0.85);
  backdrop-filter: blur(24px) saturate(180%);
  -webkit-backdrop-filter: blur(24px) saturate(180%);
  border: 1px solid var(--page-border);
  border-radius: 20px;
  box-shadow: 0 24px 64px rgba(0, 0, 0, 0.4), inset 0 1px 0 rgba(255, 255, 255, 0.06);
  overflow: hidden;
}

.config-section {
  padding: 32px 40px;
}

.section-title {
  margin-bottom: 24px;
}

.split-title {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}

.title-left {
  display: flex;
  flex-direction: column;
}

.section-title h3 {
  margin: 0 0 8px;
  font-size: 1.15rem;
  font-weight: 600;
  color: var(--page-text);
}

.section-title p {
  margin: 0;
  font-size: 0.9rem;
  color: var(--page-text-secondary);
  line-height: 1.5;
}

.path-badge {
  display: inline-block;
  margin-left: 8px;
  font-family: monospace;
  font-size: 0.8rem;
  padding: 2px 8px;
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.08);
  color: #93c5fd;
}

.section-divider {
  height: 1px;
  background: rgba(255, 255, 255, 0.08);
  margin: 0 40px;
}

.form-row {
  display: flex;
  gap: 20px;
  margin-bottom: 20px;
}

.form-row:last-child {
  margin-bottom: 0;
}

.col-2 > .field, .col-2 > .switch-field {
  flex: 1;
  min-width: 0;
}

.col-3 > .field {
  flex: 1;
  min-width: 0;
}

.sub-section-title {
  margin: 28px 0 16px;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--page-text);
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
  padding-bottom: 8px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 8px;
  flex: 1;
}

.field > span {
  font-size: 0.88rem;
  font-weight: 500;
  color: var(--page-text-secondary);
  margin-left: 2px;
}

input, select, textarea {
  width: 100%;
  box-sizing: border-box;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.1);
  color: var(--page-text);
  border-radius: 12px;
  padding: 12px 14px;
  font-size: 0.95rem;
  font-family: inherit;
  outline: none;
  transition: all 0.2s ease;
  box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.1);
}

textarea {
  resize: vertical;
  min-height: 80px;
  font-family: monospace;
  font-size: 0.88rem;
  line-height: 1.5;
}

input:focus, select:focus, textarea:focus {
  background: rgba(255, 255, 255, 0.05);
  border-color: rgba(59, 130, 246, 0.5);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.15), inset 0 2px 4px rgba(0, 0, 0, 0.1);
}

input:disabled, select:disabled {
  opacity: 0.5;
  background: rgba(0, 0, 0, 0.2);
  cursor: not-allowed;
}

input::placeholder, textarea::placeholder {
  color: var(--page-text-tertiary);
}

.helper-text {
  font-size: 0.8rem;
  color: var(--page-text-tertiary);
  margin-top: 4px;
  margin-left: 2px;
}

.outline-box {
  background: rgba(255, 255, 255, 0.02);
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 16px;
  padding: 16px 20px;
}

.outline-box.highlight {
  background: rgba(59, 130, 246, 0.05);
  border-color: rgba(59, 130, 246, 0.2);
}

.switch-field {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
}

.switch-meta {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.switch-label {
  font-size: 0.95rem;
  font-weight: 500;
  color: var(--page-text);
}

.switch-desc {
  font-size: 0.85rem;
  color: var(--page-text-secondary);
}

.mini-switch {
  padding: 12px 16px;
  margin-top: auto;
}

.switch {
  position: relative;
  display: inline-block;
  width: 44px;
  height: 24px;
  flex-shrink: 0;
}

.switch input {
  opacity: 0;
  width: 0;
  height: 0;
}

.slider {
  position: absolute;
  cursor: pointer;
  top: 0; left: 0; right: 0; bottom: 0;
  background-color: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.05);
  transition: .3s cubic-bezier(0.25, 1, 0.5, 1);
  border-radius: 34px;
}

.slider:before {
  position: absolute;
  content: "";
  height: 18px;
  width: 18px;
  left: 3px;
  bottom: 2px;
  background-color: #fff;
  transition: .3s cubic-bezier(0.25, 1, 0.5, 1);
  border-radius: 50%;
  box-shadow: 0 2px 5px rgba(0, 0, 0, 0.3);
}

input:checked + .slider {
  background-color: var(--page-accent);
  border-color: var(--page-accent);
}

input:checked + .slider:before {
  transform: translateX(18px);
}

.group-divider {
  border-top: 6px solid rgba(0, 0, 0, 0.2);
}

.accordion-section {
  background: transparent;
}

.accordion-header {
  list-style: none;
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 24px 40px;
  cursor: pointer;
  background: transparent;
  transition: background 0.2s ease;
}

.accordion-header::-webkit-details-marker {
  display: none;
}

.accordion-header:hover {
  background: rgba(255, 255, 255, 0.02);
}

.accordion-title h3 {
  margin: 0 0 6px;
  font-size: 1.05rem;
  font-weight: 600;
}

.accordion-title p {
  margin: 0;
  font-size: 0.85rem;
  color: var(--page-text-secondary);
}

.accordion-toggle-icon {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.05);
  display: grid;
  place-items: center;
  color: var(--page-text-secondary);
  transition: transform 0.3s cubic-bezier(0.25, 1, 0.5, 1), background 0.2s ease;
}

.accordion-toggle-icon svg {
  width: 18px;
  height: 18px;
}

details[open] > summary .accordion-toggle-icon {
  transform: rotate(180deg);
  background: rgba(255, 255, 255, 0.1);
  color: var(--page-text);
}

.accordion-content {
  padding: 0 40px 32px;
  animation: slideDown 0.3s ease-out forwards;
  transform-origin: top;
}

@keyframes slideDown {
  from { opacity: 0; transform: translateY(-10px); }
  to { opacity: 1; transform: translateY(0); }
}

.providers-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
  margin-bottom: 20px;
}

.provider-box {
  background: rgba(255, 255, 255, 0.015);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 16px;
  padding: 24px;
}

.provider-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
}

.provider-name {
  font-size: 1.1rem;
  font-weight: 600;
}

.provider-badge {
  background: rgba(255, 255, 255, 0.08);
  padding: 4px 10px;
  border-radius: 999px;
  font-size: 0.8rem;
  color: var(--page-text-secondary);
}

.info-strip {
  border-radius: 12px;
}

.json-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.json-item-wrapper {
  background: rgba(0, 0, 0, 0.2);
  border: 1px solid rgba(255, 255, 255, 0.05);
  border-radius: 16px;
  padding: 16px;
}

.json-item-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.json-item-head strong {
  font-size: 0.95rem;
}

.json-error {
  color: #fca5a5;
  font-size: 0.85rem;
  margin: 12px 0 0;
  padding: 8px 12px;
  background: rgba(248, 113, 113, 0.1);
  border-radius: 8px;
}

.sticky-footer {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  z-index: 50;
  display: flex;
  justify-content: center;
  padding: 0 16px 24px;
  pointer-events: none;
}

.footer-inner {
  pointer-events: auto;
  width: 100%;
  max-width: 960px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: rgba(18, 22, 33, 0.85);
  backdrop-filter: blur(24px) saturate(180%);
  -webkit-backdrop-filter: blur(24px) saturate(180%);
  border: 1px solid var(--page-border-strong);
  border-radius: 20px;
  padding: 16px 24px;
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.5), inset 0 1px 0 rgba(255, 255, 255, 0.1);
}

.footer-info {
  display: flex;
  align-items: center;
}

.info-label {
  font-size: 0.9rem;
  color: var(--page-text-secondary);
}

.footer-actions {
  display: flex;
  gap: 12px;
}

.primary-button, .secondary-button, .danger-button, .text-button {
  font-family: inherit;
  border: none;
  border-radius: 12px;
  padding: 10px 20px;
  font-size: 0.95rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s ease;
  outline: none;
}

.primary-button {
  background: var(--page-accent);
  color: #fff;
  box-shadow: 0 4px 12px rgba(59, 130, 246, 0.3);
}

.primary-button:hover:not(:disabled) {
  background: var(--page-accent-hover);
  transform: translateY(-1px);
  box-shadow: 0 6px 16px rgba(59, 130, 246, 0.4);
}

.secondary-button {
  background: rgba(255, 255, 255, 0.08);
  color: var(--page-text);
}

.secondary-button:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.12);
}

.danger-button {
  background: rgba(239, 68, 68, 0.15);
  color: #fca5a5;
  border: 1px solid rgba(239, 68, 68, 0.2);
}

.danger-button:hover:not(:disabled) {
  background: rgba(239, 68, 68, 0.25);
}

.text-button {
  background: transparent;
  color: var(--page-text-secondary);
  padding: 8px 12px;
}

.text-button:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.05);
  color: var(--page-text);
}

button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  transform: none;
  box-shadow: none;
}

/* 通知样式 */
.notice {
  padding: 14px 20px;
  border-radius: 12px;
  font-size: 0.9rem;
  line-height: 1.5;
  border: 1px solid transparent;
}

.notice.error {
  background: rgba(239, 68, 68, 0.1);
  border-color: rgba(239, 68, 68, 0.2);
  color: #fca5a5;
}

.notice.warn {
  background: rgba(234, 179, 8, 0.1);
  border-color: rgba(234, 179, 8, 0.2);
  color: #fde047;
}

.notice.info {
  background: rgba(59, 130, 246, 0.1);
  border-color: rgba(59, 130, 246, 0.2);
  color: #93c5fd;
}

@media (max-width: 768px) {
  .form-row.col-2, .form-row.col-3 {
    flex-direction: column;
    gap: 16px;
  }
  
  .config-section, .accordion-header, .accordion-content {
    padding-left: 20px;
    padding-right: 20px;
  }
  
  .section-divider {
    margin: 0 20px;
  }
  
  .footer-inner {
    flex-direction: column;
    gap: 16px;
    align-items: stretch;
  }
  
  .footer-info {
    justify-content: center;
  }
  
  .footer-actions {
    flex-direction: column-reverse; /* 主按钮在最上 */
  }
}
</style>
