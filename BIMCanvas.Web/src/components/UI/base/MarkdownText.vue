<script setup lang="ts">
import { computed } from 'vue';
import MarkdownIt from 'markdown-it';

const props = defineProps<{
  content: string;
}>();

const md = new MarkdownIt({
  html: false,
  linkify: true,
  breaks: true,
  typographer: true
});

const renderedContent = computed(() => {
  if (!props.content) return '';
  const result = md.render(props.content);
  // DEBUG: 调试日志，排查 Markdown 渲染问题
  console.log('[MarkdownText] Input (first 150 chars):', props.content.substring(0, 150));
  console.log('[MarkdownText] Output (first 300 chars):', result.substring(0, 300));
  return result;
});
</script>

<template>
  <div class="markdown-content" v-html="renderedContent"></div>
</template>

<style lang="scss">
/* Note: Not scoped because v-html content is not affected by scoped styles */
.markdown-content {
  font-size: inherit;
  line-height: 1.5;
  color: inherit;

  /* Paragraphs */
  p {
    margin-top: 0;
    margin-bottom: 0.5em;
    &:last-child {
      margin-bottom: 0;
    }
  }

  /* Lists */
  ul, ol {
    margin-top: 0;
    margin-bottom: 0.5em;
    padding-left: 1.4em;

    li {
      margin-bottom: 0.2em;
      &:last-child {
        margin-bottom: 0;
      }
    }

    /* Nested lists */
    ul, ol {
      margin-bottom: 0;
      margin-top: 0.2em;
    }
  }

  /* Headings */
  h1, h2, h3, h4, h5, h6 {
    font-weight: 600;
    margin-top: 0.8em;
    margin-bottom: 0.4em;
    line-height: 1.3;
    color: var(--text-primary);

    &:first-child {
      margin-top: 0;
    }
  }

  h1 {
    font-size: 1.5em;
    padding-bottom: 0.25em;
    border-bottom: 1px solid var(--border-dim);
  }
  h2 { font-size: 1.3em; }
  h3 { font-size: 1.15em; }
  h4 { font-size: 1.05em; }
  h5 { font-size: 1em; }
  h6 { font-size: 0.95em; color: var(--text-secondary); }

  /* Tables */
  table {
    width: 100%;
    border-collapse: collapse;
    margin: 0.6em 0;
    font-size: 0.9em;
    border-radius: 6px;
    overflow: hidden;

    th, td {
      border: 1px solid var(--border-dim);
      padding: 0.4em 0.6em;
      text-align: left;
      vertical-align: top;
    }

    th {
      background: rgba(255, 255, 255, 0.06);
      font-weight: 600;
      color: var(--text-primary);
    }

    td {
      background: rgba(0, 0, 0, 0.15);
      color: var(--text-secondary);
    }

    tr:nth-child(even) td {
      background: rgba(0, 0, 0, 0.1);
    }

    tr:hover td {
      background: rgba(255, 255, 255, 0.04);
    }
  }

  /* Code Blocks */
  pre {
    background: rgba(0, 0, 0, 0.25);
    padding: 0.8em;
    border-radius: 6px;
    overflow-x: auto;
    margin: 0.6em 0;
    border: 1px solid var(--border-dim);

    code {
      background: transparent;
      padding: 0;
      border-radius: 0;
      color: inherit;
      font-size: 0.9em;
    }
  }

  /* Inline Code */
  code {
    background: rgba(255, 255, 255, 0.08);
    padding: 0.15em 0.35em;
    border-radius: 4px;
    font-family: var(--font-mono, 'JetBrains Mono', 'Fira Code', monospace);
    font-size: 0.9em;
  }

  /* Blockquotes */
  blockquote {
    border-left: 3px solid var(--accent-primary);
    margin: 0.6em 0;
    padding: 0.4em 0.8em;
    background: rgba(59, 130, 246, 0.05);
    border-radius: 0 6px 6px 0;

    p {
      margin-bottom: 0.3em;
      &:last-child {
        margin-bottom: 0;
      }
    }
  }

  /* Links */
  a {
    color: var(--accent-primary);
    text-decoration: none;
    &:hover {
      text-decoration: underline;
    }
  }

  /* Horizontal Rule */
  hr {
    border: none;
    border-top: 1px solid var(--border-dim);
    margin: 0.8em 0;
  }

  /* Images */
  img {
    max-width: 100%;
    height: auto;
    border-radius: 4px;
  }

  /* Strong & Emphasis */
  strong {
    font-weight: 600;
    color: var(--text-primary);
  }
}
</style>
