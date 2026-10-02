<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { api, type CandidateItem, type ResolveResult } from '@/api/client'
import UrlInput from '@/components/UrlInput.vue'
import BatchPreview, { type PreviewRow } from '@/components/BatchPreview.vue'
import SavedResult from '@/components/SavedResult.vue'
import AggregationPreview from '@/components/AggregationPreview.vue'
import TimelineList from '@/components/TimelineList.vue'
import { Button } from '@/components/ui/button'

const router = useRouter()
const saved = ref<import('@/api/client').CollectResult | null>(null)
const candidates = ref<CandidateItem[] | null>(null)
const batch = ref<{
  rows: PreviewRow[]
  heading: string
  skipped: number
  groupId?: string
  groupName?: string
} | null>(null)
const listKey = ref(0)

/**
 * 解析结果分流：
 * - 单条且原文就是完整 URL → 沿用单条卡片（含"本页还包含 N 个条目"展开）
 * - 其余（多条 / slug / 关键词）→ 批量预览
 */
function onResolved(result: ResolveResult & { groupId?: string; groupName?: string; skipped?: number }) {
  const { resolved, groupId, groupName } = result

  if (!groupId && resolved.length === 1 && /^https?:\/\//i.test(resolved[0].input)) {
    const only = resolved[0]
    saved.value = {
      id: '',
      url: only.url,
      title: only.title,
      translation: only.translation,
      sourceUrl: null,
      isFallbackTitle: only.isFallbackTitle,
      createdAt: new Date().toISOString(),
      tags: only.tags,
      groupId: null,
      markdown: '',
      itemCount: 0,
    }
    listKey.value++
    return
  }

  if (!resolved.length) return

  batch.value = {
    rows: resolved.map((r) => ({
      url: r.url,
      title: r.title,
      translation: r.translation,
      tags: r.tags,
      isExisting: r.isExisting,
    })),
    heading: groupName ? `${groupName} · ${resolved.length} 条` : `解析到 ${resolved.length} 条链接`,
    skipped: result.skipped ?? resolved.filter((r) => r.isExisting).length,
    groupId: result.groupId,
    groupName,
  }
}

function onBatchSaved(n: number) {
  toast.success(`已收藏 ${n} 条`)
  batch.value = null
  saved.value = null
  listKey.value++
}

function onExtracted(list: CandidateItem[], _sourceUrl: string) {
  candidates.value = list
}

async function logout() {
  try {
    await api('/api/auth/logout', { method: 'POST' })
  } catch {
    // 清 cookie 失败不阻断跳转
  }
  router.push('/login')
}
</script>

<template>
  <!-- 单列：输入框吸顶，下方时间线滚动加载 -->
  <div class="mx-auto max-w-2xl">
    <header class="sticky top-0 z-10 space-y-3 border-b bg-background/85 px-4 py-3 backdrop-blur">
      <div class="flex items-center justify-between">
        <h1 class="text-sm font-semibold">linkstash</h1>
        <Button variant="ghost" size="xs" @click="logout">退出</Button>
      </div>
      <UrlInput @resolved="onResolved" />
    </header>

    <main class="space-y-4 px-4 py-4">
      <SavedResult
        v-if="saved"
        :result="saved"
        @extracted="onExtracted"
        @deleted="saved = null"
      />

      <BatchPreview
        v-if="batch"
        :rows="batch.rows"
        :heading="batch.heading"
        :skipped="batch.skipped"
        :group-id="batch.groupId"
        :group-name="batch.groupName"
        @saved="onBatchSaved"
        @close="batch = null"
      />

      <AggregationPreview
        v-if="candidates"
        :candidates="candidates"
        @saved="onBatchSaved"
        @close="candidates = null"
      />

      <TimelineList :key="listKey" />
    </main>
  </div>
</template>
