<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import GlassButton from './base/GlassButton.vue';
import { AtlasService, type AtlasSceneItem } from '../../services/ProjectService';

interface Props {
    visible: boolean;
    defaultName?: string;
}

const props = defineProps<Props>();

const emit = defineEmits<{
    (e: 'create', projectName: string, sceneId: string | null): void;
    (e: 'cancel'): void;
}>();

const BLANK_ID = '__blank__';

const projectName = ref(props.defaultName ?? '');
const selectedSceneId = ref<string>(BLANK_ID); // 默认选中空白
const atlasScenes = ref<AtlasSceneItem[]>([]);
const atlasAvailable = ref(false);

onMounted(async () => {
    projectName.value = props.defaultName ?? '';
    try {
        const resp = await AtlasService.fetchScenes();
        atlasAvailable.value = resp.available;
        atlasScenes.value = resp.scenes ?? [];
    } catch {
        atlasAvailable.value = false;
    }
});

const canCreate = computed(() => projectName.value.trim().length > 0);

const selectCard = (id: string, scene?: AtlasSceneItem) => {
    selectedSceneId.value = id;
    // 选中场景时，若项目名是默认名则自动替换为场景名
    if (id !== BLANK_ID && scene) {
        if (!projectName.value.trim() || projectName.value === props.defaultName) {
            projectName.value = scene.displayName;
        }
    }
    // 切回空白时，若项目名是某个场景名则恢复默认
    if (id === BLANK_ID) {
        const isSceneName = atlasScenes.value.some(s => s.displayName === projectName.value);
        if (isSceneName) {
            projectName.value = props.defaultName ?? '';
        }
    }
};

const handleCreate = () => {
    const name = projectName.value.trim();
    if (!name) return;
    const sceneId = selectedSceneId.value === BLANK_ID ? null : selectedSceneId.value;
    emit('create', name, sceneId);
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
                    <!-- Header -->
                    <div class="dialog-header">
                        <h3>新建项目</h3>
                    </div>

                    <!-- 场景选择（空白 + atlas 场景并列） -->
                    <div class="scene-grid" :class="{ 'no-atlas': !atlasAvailable }">
                        <!-- 空白项目卡片 -->
                        <div
                            class="scene-card"
                            :class="{ selected: selectedSceneId === '__blank__' }"
                            @click="selectCard('__blank__')"
                        >
                            <div class="scene-check">
                                <svg viewBox="0 0 24 24" width="10" height="10" fill="none" stroke="currentColor" stroke-width="3">
                                    <polyline points="20 6 9 17 4 12"/>
                                </svg>
                            </div>
                            <div class="scene-icon blank-icon">
                                <svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" stroke-width="1.5">
                                    <rect x="3" y="3" width="18" height="18" rx="2"/>
                                </svg>
                            </div>
                            <div class="scene-info">
                                <div class="scene-name">空白项目</div>
                                <div class="scene-meta">从零开始</div>
                            </div>
                        </div>

                        <!-- Atlas 场景卡片 -->
                        <template v-if="atlasAvailable">
                            <div
                                v-for="scene in atlasScenes"
                                :key="scene.id"
                                class="scene-card"
                                :class="{ selected: selectedSceneId === scene.id }"
                                @click="selectCard(scene.id, scene)"
                            >
                                <div class="scene-check">
                                    <svg viewBox="0 0 24 24" width="10" height="10" fill="none" stroke="currentColor" stroke-width="3">
                                        <polyline points="20 6 9 17 4 12"/>
                                    </svg>
                                </div>
                                <div class="scene-icon">
                                    <svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" stroke-width="1.5">
                                        <rect x="3" y="3" width="18" height="18" rx="2"/>
                                        <path d="M3 9h18M9 21V9"/>
                                    </svg>
                                </div>
                                <div class="scene-info">
                                    <div class="scene-name">{{ scene.displayName }}</div>
                                    <div class="scene-meta">{{ scene.description }}</div>
                                </div>
                            </div>
                        </template>
                    </div>

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

                    <!-- 操作按钮 -->
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
    width: 560px;
    max-width: 94vw;
    box-shadow:
        0 8px 40px rgba(0, 0, 0, 0.4),
        0 0 0 1px rgba(255, 255, 255, 0.05) inset;
}

.dialog-header {
    margin-bottom: 18px;
}

.dialog-header h3 {
    margin: 0;
    font-size: 16px;
    font-weight: 600;
    color: var(--text-primary);
}

/* Scene grid */
.scene-grid {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 8px;
    margin-bottom: 16px;
}

.scene-grid.no-atlas {
    grid-template-columns: 1fr;
}

.scene-card {
    position: relative;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    padding: 16px 10px 12px;
    background: rgba(255, 255, 255, 0.03);
    border: 1px solid var(--border-subtle);
    border-radius: 10px;
    cursor: pointer;
    transition: all 0.15s ease;
    text-align: center;
    user-select: none;
}

.scene-card:hover {
    background: rgba(255, 255, 255, 0.06);
    border-color: rgba(255, 255, 255, 0.15);
}

.scene-card.selected {
    background: rgba(59, 130, 246, 0.1);
    border-color: var(--accent-blue);
    box-shadow: 0 0 0 1px var(--accent-blue) inset;
}

/* 选中勾 */
.scene-check {
    position: absolute;
    top: 7px;
    right: 7px;
    width: 16px;
    height: 16px;
    border-radius: 50%;
    background: var(--accent-blue);
    display: flex;
    align-items: center;
    justify-content: center;
    opacity: 0;
    transform: scale(0.5);
    transition: all 0.15s ease;
    color: white;
}

.scene-card.selected .scene-check {
    opacity: 1;
    transform: scale(1);
}

.scene-icon {
    color: var(--accent-blue);
    opacity: 0.8;
}

.blank-icon {
    color: var(--text-tertiary);
    opacity: 0.5;
}

.scene-card.selected .scene-icon {
    opacity: 1;
}

.scene-info {
    width: 100%;
}

.scene-name {
    font-size: 0.82rem;
    font-weight: 600;
    color: var(--text-primary);
    margin-bottom: 3px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.scene-meta {
    font-size: 0.7rem;
    color: var(--text-tertiary);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

/* Field */
.field {
    margin-bottom: 18px;
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
