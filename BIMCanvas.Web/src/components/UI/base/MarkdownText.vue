<script setup lang="ts">
import MarkdownRender from 'markstream-vue'
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  content: string
  density?: 'default' | 'chat-compact'
}>(), {
  density: 'default'
})

const wrapperClass = computed(() => [
  'markdown-text',
  props.density === 'chat-compact' ? 'markdown-text--chat-compact' : ''
])
</script>

<template>
  <div :class="wrapperClass">
    <MarkdownRender
      :content="content"
      :is-dark="true"
      :max-live-nodes="0"
    />
  </div>
</template>

<style scoped lang="scss">
.markdown-text--chat-compact {
  :deep(.markdown-renderer) {
    margin: 0;
    padding: 0;
  }

  :deep(.node-slot),
  :deep(.node-content),
  :deep(.node-space) {
    margin: 0;
    padding: 0;
  }

  :deep(p) {
    margin: 0;
  }

  :deep(.paragraph-node) {
    margin: 0.3em 0;
  }

  :deep(.heading-1) {
    font-size: 1.05rem;
    margin: 0.7em 0 0.35em 0;
  }

  :deep(.heading-2) {
    font-size: 0.95rem;
    margin: 0.6em 0 0.3em 0;
  }

  :deep(.heading-3) {
    font-size: 0.88rem;
    margin: 0.5em 0 0.25em 0;
  }

  :deep(.heading-4),
  :deep(.heading-5),
  :deep(.heading-6) {
    font-size: 0.82rem;
    margin: 0.45em 0 0.2em 0;
  }

  :deep(.list-node) {
    margin: 0.25em 0;
    padding-left: 1.1em;
  }

  :deep(.list-item) {
    margin: 0;
    padding: 0;
  }

  :deep(.table-node-wrapper) {
    margin: 0.5em 0;
  }

  :deep(.table-node) {
    --table-border: rgba(255, 255, 255, 0.25);
    margin: 0;
  }

  :deep(.table-node th),
  :deep(.table-node td) {
    border: 1px solid rgba(255, 255, 255, 0.2);
    padding: 0.3em 0.5em;
  }

  :deep(.table-node th) {
    background: rgba(255, 255, 255, 0.05);
  }

  :deep(.blockquote) {
    margin: 0.3em 0;
    padding-left: 0.6em;
  }

  :deep(.code-block-container) {
    margin: 0.3em 0;
  }

  :deep(.hr-node) {
    margin: 0.5em 0;
  }
}
</style>
