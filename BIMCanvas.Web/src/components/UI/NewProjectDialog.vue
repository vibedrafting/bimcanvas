<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import GlassButton from './base/GlassButton.vue';
import { SceneService, type SceneItem } from '../../services/ProjectService';

interface Props {
    visible: boolean;
    defaultName?: string;
}

const props = defineProps<Props>();

const emit = defineEmits<{
    (e: 'create', projectName: string, pluginId: string | null, sceneId: string | null): void;
    (e: 'cancel'): void;
}>();

const BLANK_ID = '__blank__';

const projectName = ref(props.defaultName ?? '');
const selectedSceneId = ref<string>(BLANK_ID);
const scenes = ref<SceneItem[]>([]);
const scenesAvailable = ref(false);
const loading = ref(true);

onMounted(async () => {
    projectName.value = props.defaultName ?? '';
    try {
        const resp = await SceneService.fetchScenes();
        scenesAvailable.value = resp.available;
        scenes.value = resp.scenes ?? [];
    } catch {
        scenesAvailable.value = false;
    } finally {
        loading.value = false;
    }
});

const canCreate = computed(() => projectName.value.trim().length > 0);

const selectedScene = computed(() =>
    selectedSceneId.value === BLANK_ID ? null : scenes.value.find(s => s.id === selectedSceneId.value) ?? null
);

const selectCard = (id: string, scene?: SceneItem) => {
    selectedSceneId.value = id;
    if (id !== BLANK_ID && scene) {
        if (!projectName.value.trim() || projectName.value === props.defaultName) {
            projectName.value = scene.displayName;
        }
    }
    if (id === BLANK_ID) {
        const isSceneName = scenes.value.some(s => s.displayName === projectName.value);
        if (isSceneName) {
            projectName.value = props.defaultName ?? '';
        }
    }
};

const handleCreate = () => {
    const name = projectName.value.trim();
    if (!name) return;
    if (selectedSceneId.value === BLANK_ID) {
        emit('create', name, null, null);
    } else {
        const selected = scenes.value.find(s => s.id === selectedSceneId.value);
        emit('create', name, selected?.pluginId ?? null, selectedSceneId.value);
    }
};

const handleCancel = () => emit('cancel');

const handleKeydown = (e: KeyboardEvent) => {
    if (e.key === 'Enter' && canCreate.value) handleCreate();
    if (e.key === 'Escape') handleCancel();
};
</script>

<template>
    <Teleport to="body">
        <Transition name="dialog">
            <div v-if="visible" class="dialog-overlay" @click.self="handleCancel">
                <div class="new-project-dialog" @keydown="handleKeydown">
                    <div class="dialog-header">
                        <h3>新建项目</h3>
                    </div>

                    <!-- 场景列表 + 详情 两栏布局（有场景时） -->
                    <div v-if="!loading && scenesAvailable" class="scene-layout">
                        <!-- 左栏：场景列表 -->
                        <div class="scene-list">
                            <div
                                class="scene-list-item"
                                :class="{ selected: selectedSceneId === '__blank__' }"
                                @click="selectCard('__blank__')"
                            >
                                <div class="item-icon blank-icon">
                                    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.5">
                                        <rect x="3" y="3" width="18" height="18" rx="2"/>
                                    </svg>
                                </div>
                                <span class="item-name">空白项目</span>
                                <div class="item-check">
                                    <svg viewBox="0 0 24 24" width="9" height="9" fill="none" stroke="currentColor" stroke-width="3">
                                        <polyline points="20 6 9 17 4 12"/>
                                    </svg>
                                </div>
                            </div>

                            <div
                                v-for="scene in scenes"
                                :key="scene.id"
                                class="scene-list-item"
                                :class="{ selected: selectedSceneId === scene.id }"
                                @click="selectCard(scene.id, scene)"
                            >
                                <div class="item-icon">
                                    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.5">
                                        <rect x="3" y="3" width="18" height="18" rx="2"/>
                                        <path d="M3 9h18M9 21V9"/>
                                    </svg>
                                </div>
                                <span class="item-name">{{ scene.displayName }}</span>
                                <div class="item-check">
                                    <svg viewBox="0 0 24 24" width="9" height="9" fill="none" stroke="currentColor" stroke-width="3">
                                        <polyline points="20 6 9 17 4 12"/>
                                    </svg>
                                </div>
                            </div>
                        </div>

                        <!-- 右栏：选中场景详情 -->
                        <div class="scene-detail">
                            <template v-if="selectedSceneId === '__blank__'">
                                <div class="detail-preview blank-preview">
                                    <svg viewBox="0 0 24 24" width="36" height="36" fill="none" stroke="currentColor" stroke-width="1" opacity="0.3">
                                        <rect x="3" y="3" width="18" height="18" rx="2"/>
                                    </svg>
                                </div>
                                <div class="detail-title">空白项目</div>
                                <div class="detail-desc">从零开始，完全自由的画布。</div>
                            </template>
                            <template v-else-if="selectedScene">
                                <div class="detail-preview">
                                    <svg viewBox="0 0 24 24" width="36" height="36" fill="none" stroke="currentColor" stroke-width="1" opacity="0.5">
                                        <rect x="3" y="3" width="18" height="18" rx="2"/>
                                        <path d="M3 9h18M9 21V9"/>
                                    </svg>
                                </div>
                                <div class="detail-title">{{ selectedScene.displayName }}</div>
                                <div v-if="selectedScene.description" class="detail-desc">{{ selectedScene.description }}</div>
                                <div class="detail-meta">
                                    <span v-if="selectedScene.area" class="meta-chip">{{ selectedScene.area }}㎡</span>
                                    <span v-for="tag in selectedScene.tags" :key="tag" class="meta-chip">{{ tag }}</span>
                                </div>
                                <div v-if="selectedScene.rooms?.length" class="detail-rooms">
                                    <span v-for="room in selectedScene.rooms" :key="room" class="room-chip">{{ room }}</span>
                                </div>
                            </template>
                        </div>
                    </div>

                    <!-- 无场景时：仅显示空白项目提示 -->
                    <div v-else-if="!loading" class="no-scenes-hint">
                        从零开始新建空白项目。安装场景 plugin 后可从模板快速开始。
                    </div>

                    <!-- 加载中 -->
                    <div v-else class="loading-hint">加载场景中...</div>

                    <!-- 项目名称 -->
                    <div class="field">
                        <input
                            v-model="projectName"
                            class="name-input"
                            placeholder="项目名称"
                            autofocus
                            maxlength="64"
                        />
                    </div>

                    <div class="dialog-actions">
                        <GlassButton variant="ghost" @click="handleCancel">取消</GlassButton>
                        <GlassButton variant="primary" :disabled="!canCreate" @click="handleCreate">创建</GlassButton>
                    </div>
                </div>
            </div>
        </Transition>
    </Teleport>
</template>

<style scoped>
.dialog-overlay {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.6);
    backdrop-filter: blur(4px);
    -webkit-backdrop-filter: blur(4px);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 9999;
}

.new-project-dialog {
    background: var(--bg-surface);
    border: 1px solid var(--border-subtle);
    border-radius: 14px;
    padding: 24px;
    width: 600px;
    max-width: 94vw;
    box-shadow:
        0 8px 40px rgba(0, 0, 0, 0.4),
        0 0 0 1px rgba(255, 255, 255, 0.05) inset;
}

.dialog-header {
    margin-bottom: 16px;
}

.dialog-header h3 {
    margin: 0;
    font-size: 16px;
    font-weight: 600;
    color: var(--text-primary);
}

/* Two-column layout */
.scene-layout {
    display: grid;
    grid-template-columns: 180px 1fr;
    gap: 12px;
    margin-bottom: 16px;
    min-height: 200px;
}

/* Left: scene list */
.scene-list {
    display: flex;
    flex-direction: column;
    gap: 2px;
    overflow-y: auto;
    max-height: 240px;
    padding-right: 4px;
}

.scene-list::-webkit-scrollbar {
    width: 4px;
}
.scene-list::-webkit-scrollbar-track {
    background: transparent;
}
.scene-list::-webkit-scrollbar-thumb {
    background: rgba(255,255,255,0.1);
    border-radius: 2px;
}

.scene-list-item {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 7px 10px;
    border-radius: 7px;
    cursor: pointer;
    transition: background 0.12s ease;
    user-select: none;
    position: relative;
}

.scene-list-item:hover {
    background: rgba(255, 255, 255, 0.05);
}

.scene-list-item.selected {
    background: rgba(59, 130, 246, 0.12);
}

.item-icon {
    flex-shrink: 0;
    color: var(--accent-blue);
    opacity: 0.7;
    display: flex;
}

.blank-icon {
    color: var(--text-tertiary);
    opacity: 0.5;
}

.scene-list-item.selected .item-icon {
    opacity: 1;
}

.item-name {
    flex: 1;
    font-size: 0.82rem;
    font-weight: 500;
    color: var(--text-secondary);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    min-width: 0;
}

.scene-list-item.selected .item-name {
    color: var(--text-primary);
}

.item-check {
    flex-shrink: 0;
    width: 14px;
    height: 14px;
    border-radius: 50%;
    background: var(--accent-blue);
    display: flex;
    align-items: center;
    justify-content: center;
    opacity: 0;
    transform: scale(0.4);
    transition: all 0.12s ease;
    color: white;
}

.scene-list-item.selected .item-check {
    opacity: 1;
    transform: scale(1);
}

/* Right: detail panel */
.scene-detail {
    background: rgba(255, 255, 255, 0.02);
    border: 1px solid var(--border-subtle);
    border-radius: 10px;
    padding: 16px;
    display: flex;
    flex-direction: column;
    gap: 8px;
    min-width: 0;
}

.detail-preview {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 64px;
    background: rgba(255, 255, 255, 0.02);
    border-radius: 6px;
    color: var(--accent-blue);
}

.blank-preview {
    color: var(--text-tertiary);
}

.detail-title {
    font-size: 0.9rem;
    font-weight: 600;
    color: var(--text-primary);
}

.detail-desc {
    font-size: 0.78rem;
    color: var(--text-secondary);
    line-height: 1.5;
}

.detail-meta {
    display: flex;
    flex-wrap: wrap;
    gap: 5px;
}

.meta-chip {
    font-size: 0.7rem;
    padding: 2px 7px;
    border-radius: 4px;
    background: rgba(59, 130, 246, 0.12);
    color: var(--accent-blue);
    white-space: nowrap;
}

.detail-rooms {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    margin-top: 2px;
}

.room-chip {
    font-size: 0.68rem;
    padding: 2px 6px;
    border-radius: 4px;
    background: rgba(255, 255, 255, 0.05);
    color: var(--text-tertiary);
    white-space: nowrap;
}

/* No-scenes fallback */
.no-scenes-hint {
    font-size: 0.82rem;
    color: var(--text-tertiary);
    padding: 12px 0 16px;
    line-height: 1.5;
}

.loading-hint {
    font-size: 0.82rem;
    color: var(--text-tertiary);
    padding: 20px 0;
    text-align: center;
}

/* Field */
.field {
    margin-bottom: 16px;
}

.name-input {
    width: 100%;
    box-sizing: border-box;
    background: var(--bg-canvas);
    border: 1px solid var(--border-subtle);
    border-radius: 8px;
    padding: 10px 14px;
    color: var(--text-primary);
    font-size: 0.9rem;
    font-family: var(--font-sans);
    outline: none;
    transition: border-color 0.15s;
}

.name-input:focus {
    border-color: var(--accent-blue);
    box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
}

.name-input::placeholder {
    color: var(--text-tertiary);
}

/* Actions */
.dialog-actions {
    display: flex;
    gap: 10px;
    justify-content: flex-end;
}

/* Transition */
.dialog-enter-active,
.dialog-leave-active {
    transition: all 0.18s ease;
}

.dialog-enter-from,
.dialog-leave-to {
    opacity: 0;
}

.dialog-enter-from .new-project-dialog,
.dialog-leave-to .new-project-dialog {
    transform: scale(0.97) translateY(-6px);
    opacity: 0;
}

.dialog-enter-active .new-project-dialog,
.dialog-leave-active .new-project-dialog {
    transition: all 0.18s cubic-bezier(0.19, 1, 0.22, 1);
}
</style>
