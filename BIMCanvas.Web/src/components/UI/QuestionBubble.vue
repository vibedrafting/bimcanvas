<script setup lang="ts">
import { computed, nextTick, reactive, ref } from 'vue'
import type { ChatBubble, UserQuestion } from '../../types/agent'

const props = defineProps<{
  bubble: ChatBubble
}>()

const emit = defineEmits<{
  submit: [bubble: ChatBubble]
  cancel: [bubble: ChatBubble]
}>()

// 每个问题的选中状态: questionText -> Set<selectedLabel>
const selections = reactive<Record<string, Set<string>>>({})
// Other 输入框的展开状态和内容
const otherExpanded = reactive<Record<string, boolean>>({})
const otherText = reactive<Record<string, string>>({})

// 初始化
if (props.bubble.questions) {
  for (const q of props.bubble.questions) {
    selections[q.question] = new Set()
    otherExpanded[q.question] = false
    otherText[q.question] = ''
  }
}

const isSubmitted = computed(() => props.bubble.questionSubmitted)

const isSubmittable = computed(() => {
  if (!props.bubble.questions) return false
  return props.bubble.questions.every(q => {
    const set = selections[q.question]
    const hasOther = otherExpanded[q.question] && otherText[q.question].trim()
    return (set?.size ?? 0) > 0 || hasOther
  })
})

const toggleOption = (question: UserQuestion, label: string) => {
  if (isSubmitted.value) return
  const set = selections[question.question]
  if (!set) {
    console.warn('[QuestionBubble] No selection set for question:', question.question)
    return
  }
  if (question.multiSelect) {
    if (set.has(label)) set.delete(label)
    else set.add(label)
  } else {
    set.clear()
    otherExpanded[question.question] = false
    // 不清空 otherText，保留用户已输入内容，切回 Other 时恢复
    set.add(label)
  }
  console.log(`[QuestionBubble] toggleOption: "${label}", set size: ${set.size}, isSubmittable: ${isSubmittable.value}`)
}

const toggleOther = (question: UserQuestion) => {
  if (isSubmitted.value) return
  const key = question.question
  if (!question.multiSelect) {
    // 单选：选 Other 时清除其他
    selections[key].clear()
  }
  otherExpanded[key] = !otherExpanded[key]
  if (!otherExpanded[key]) {
    otherText[key] = ''
  }
}

const otherInputRefs = ref<HTMLInputElement[]>([])

const isOtherActive = (question: UserQuestion) => {
  return otherExpanded[question.question] && otherText[question.question].trim()
}

const handleOtherRowClick = (question: UserQuestion) => {
  if (isSubmitted.value) return
  const key = question.question
  if (!otherExpanded[key]) {
    // 首次展开
    if (!question.multiSelect) {
      selections[key].clear()
    }
    otherExpanded[key] = true
    nextTick(() => {
      // 聚焦到输入框
      const inputs = otherInputRefs.value
      if (inputs?.length) {
        inputs[inputs.length - 1]?.focus()
      }
    })
  }
  // 已展开时点击行不收起（避免误操作），用户可以通过选其他选项来切走
}

const buildAnswers = (): Record<string, string> => {
  const answers: Record<string, string> = {}
  for (const q of (props.bubble.questions || [])) {
    const parts: string[] = [...(selections[q.question] || [])]
    // 仅在 Other 展开时才计入自定义文本，避免切走后残留文本被带入
    if (otherExpanded[q.question]) {
      const other = otherText[q.question]?.trim()
      if (other) parts.push(other)
    }
    answers[q.question] = parts.join(', ')
  }
  return answers
}

const handleSubmit = () => {
  console.log(`[QuestionBubble] handleSubmit called, isSubmittable: ${isSubmittable.value}, isSubmitted: ${isSubmitted.value}`)
  if (!isSubmittable.value || isSubmitted.value) return
  props.bubble.questionAnswers = buildAnswers()
  console.log('[QuestionBubble] emitting submit, answers:', props.bubble.questionAnswers)
  emit('submit', props.bubble)
}

const handleCancel = () => {
  if (isSubmitted.value) return
  emit('cancel', props.bubble)
}

// 提交后的摘要
const answerSummary = computed(() => {
  const answers = props.bubble.questionAnswers || {}
  return Object.values(answers).filter(v => v).join(' / ')
})
</script>

<template>
  <div class="question-bubble" :class="{ submitted: isSubmitted }">
    <template v-if="!isSubmitted">
      <!-- 每个问题块 -->
      <div v-for="(q, qIdx) in bubble.questions" :key="q.question" class="question-block">
        <div class="question-text">
          {{ q.question }}
          <span v-if="q.multiSelect" class="multi-hint">(可多选)</span>
        </div>

        <div class="option-list">
          <button
            v-for="(opt, idx) in q.options"
            :key="opt.label"
            class="option-row"
            :class="{ selected: selections[q.question]?.has(opt.label) }"
            @click="toggleOption(q, opt.label)"
          >
            <span class="option-indicator">
              <span v-if="q.multiSelect" class="check-box" :class="{ checked: selections[q.question]?.has(opt.label) }">
                <svg v-if="selections[q.question]?.has(opt.label)" viewBox="0 0 12 12" fill="none">
                  <path d="M2.5 6L5 8.5L9.5 3.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>
              </span>
              <span v-else class="radio-dot" :class="{ checked: selections[q.question]?.has(opt.label) }" />
            </span>
            <span class="option-index">{{ idx + 1 }}.</span>
            <span class="option-label">{{ opt.label }}</span>
            <span v-if="opt.description" class="option-info" :title="opt.description">
              <svg viewBox="0 0 16 16" fill="none">
                <circle cx="8" cy="8" r="6.5" stroke="currentColor" stroke-width="1"/>
                <path d="M8 7V11" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/>
                <circle cx="8" cy="5" r="0.7" fill="currentColor"/>
              </svg>
            </span>
          </button>

          <!-- Other 行（内联输入） -->
          <div
            class="option-row option-row-other"
            :class="{ selected: isOtherActive(q) }"
            @click="handleOtherRowClick(q)"
          >
            <span class="option-indicator">
              <span v-if="q.multiSelect" class="check-box" :class="{ checked: isOtherActive(q) }">
                <svg v-if="isOtherActive(q)" viewBox="0 0 12 12" fill="none">
                  <path d="M2.5 6L5 8.5L9.5 3.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>
              </span>
              <span v-else class="radio-dot" :class="{ checked: isOtherActive(q) }" />
            </span>
            <span class="option-index">{{ q.options.length + 1 }}.</span>
            <!-- 未展开且无文本：显示"其他..." -->
            <span v-if="!otherExpanded[q.question] && !otherText[q.question]" class="option-label option-label-other">其他...</span>
            <!-- 未展开但有文本：显示已输入内容（灰色） -->
            <span v-else-if="!otherExpanded[q.question] && otherText[q.question]" class="option-label option-label-saved">{{ otherText[q.question] }}</span>
            <!-- 展开：显示输入框 -->
            <input
              v-else
              ref="otherInputRefs"
              v-model="otherText[q.question]"
              class="other-inline-input"
              type="text"
              placeholder="输入自定义答案..."
              @click.stop
            />
          </div>
        </div>

        <!-- 问题间分隔 -->
        <div v-if="qIdx < (bubble.questions?.length ?? 0) - 1" class="question-divider" />
      </div>

      <!-- 底栏 -->
      <div class="action-bar">
        <button class="btn-ignore" @click="handleCancel">
          忽略 <kbd>ESC</kbd>
        </button>
        <button class="btn-submit" :disabled="!isSubmittable" @click="handleSubmit">
          提交
          <svg class="submit-check" viewBox="0 0 12 12" fill="none">
            <path d="M2.5 6L5 8.5L9.5 3.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
        </button>
      </div>
    </template>

    <!-- 已提交摘要 -->
    <template v-else>
      <div class="question-result">
        <span v-if="answerSummary" class="result-text">已选择: {{ answerSummary }}</span>
        <span v-else class="result-skipped">已跳过</span>
      </div>
    </template>
  </div>
</template>

<style scoped lang="scss">
.question-bubble {
  background: rgba(20, 20, 22, 0.6);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 10px;
  margin: 6px 0;
  padding: 16px;
  font-family: 'Inter', system-ui, sans-serif;

  &.submitted {
    padding: 8px 12px;
  }
}

// ── 问题块 ──

.question-block {
  & + .question-block {
    margin-top: 4px;
  }
}

.question-text {
  font-size: 13px;
  font-weight: 600;
  color: rgba(255, 255, 255, 0.9);
  line-height: 1.5;
  margin-bottom: 10px;
}

.multi-hint {
  font-weight: 400;
  color: rgba(255, 255, 255, 0.35);
  font-size: 12px;
  margin-left: 4px;
}

.question-divider {
  height: 1px;
  background: rgba(255, 255, 255, 0.06);
  margin: 12px 0;
}

// ── 选项列表 ──

.option-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.option-row {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 7px 10px;
  border: none;
  border-left: 3px solid transparent;
  border-radius: 6px;
  background: transparent;
  cursor: pointer;
  transition: all 0.12s ease;
  text-align: left;
  color: rgba(255, 255, 255, 0.75);
  font-family: inherit;

  &:hover {
    background: rgba(255, 255, 255, 0.04);
  }

  &.selected {
    background: rgba(59, 130, 246, 0.1);
    border-left-color: #3b82f6;
    color: rgba(255, 255, 255, 0.95);
  }
}

// ── 选中指示器 ──

.option-indicator {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 14px;
  height: 14px;
}

.radio-dot {
  display: block;
  width: 12px;
  height: 12px;
  border-radius: 50%;
  border: 1.5px solid rgba(255, 255, 255, 0.25);
  transition: all 0.12s ease;

  &.checked {
    border-color: #3b82f6;
    background: #3b82f6;
    box-shadow: inset 0 0 0 2.5px rgba(20, 20, 22, 0.8);
  }
}

.check-box {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 12px;
  height: 12px;
  border-radius: 3px;
  border: 1.5px solid rgba(255, 255, 255, 0.25);
  transition: all 0.12s ease;
  color: #fff;

  svg {
    width: 10px;
    height: 10px;
  }

  &.checked {
    border-color: #3b82f6;
    background: #3b82f6;
  }
}

// ── 序号 + 标签 + 信息图标 ──

.option-index {
  color: rgba(255, 255, 255, 0.35);
  font-size: 12px;
  min-width: 18px;
  flex-shrink: 0;
  font-variant-numeric: tabular-nums;
}

.option-label {
  font-size: 13px;
  font-weight: 400;
  flex: 1;
  min-width: 0;
}

.option-label-other {
  color: rgba(255, 255, 255, 0.5);
  font-style: italic;
}

.option-label-saved {
  font-size: 13px;
  font-weight: 400;
  flex: 1;
  min-width: 0;
  color: rgba(255, 255, 255, 0.45);
}

.option-info {
  flex-shrink: 0;
  margin-left: auto;
  color: rgba(255, 255, 255, 0.2);
  cursor: help;
  display: flex;
  align-items: center;
  transition: color 0.12s ease;

  svg {
    width: 14px;
    height: 14px;
  }

  &:hover {
    color: rgba(255, 255, 255, 0.5);
  }
}

// ── Other 行内联输入 ──

.option-row-other {
  box-sizing: border-box; // div 不像 button 默认 border-box，需显式设置
  overflow: hidden;

  .option-label-other {
    flex-shrink: 0;
  }
}

.other-inline-input {
  flex: 1;
  width: 0; // 配合 flex:1 防止 input 固有宽度撑开
  min-width: 0;
  padding: 2px 0;
  border: none;
  border-bottom: 1px solid rgba(255, 255, 255, 0.15);
  border-radius: 0;
  background: transparent;
  color: rgba(255, 255, 255, 0.9);
  font-size: 13px;
  font-family: inherit;
  outline: none;

  &:focus {
    border-bottom-color: #3b82f6;
  }

  &::placeholder {
    color: rgba(255, 255, 255, 0.25);
    font-style: italic;
  }
}

// ── 底栏 ──

.action-bar {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 12px;
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
}

.btn-ignore {
  padding: 4px 8px;
  border: none;
  border-radius: 4px;
  background: transparent;
  color: rgba(255, 255, 255, 0.35);
  font-size: 12px;
  cursor: pointer;
  font-family: inherit;

  kbd {
    display: inline-block;
    padding: 0 4px;
    margin-left: 2px;
    border: 1px solid rgba(255, 255, 255, 0.12);
    border-radius: 3px;
    font-size: 10px;
    font-family: inherit;
    color: rgba(255, 255, 255, 0.3);
    line-height: 1.4;
  }

  &:hover {
    color: rgba(255, 255, 255, 0.6);

    kbd {
      border-color: rgba(255, 255, 255, 0.2);
      color: rgba(255, 255, 255, 0.5);
    }
  }
}

.btn-submit {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 5px 14px;
  border: none;
  border-radius: 14px;
  background: #3b82f6;
  color: #fff;
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  font-family: inherit;
  transition: all 0.12s ease;

  .submit-check {
    width: 12px;
    height: 12px;
  }

  &:disabled {
    opacity: 0.35;
    cursor: not-allowed;
  }

  &:not(:disabled):hover {
    background: #2563eb;
  }
}

// ── 已提交摘要 ──

.question-result {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 11px;
}

.result-text {
  color: rgba(255, 255, 255, 0.6);
}

.result-skipped {
  color: rgba(255, 255, 255, 0.35);
  font-style: italic;
}
</style>
