<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { api, type CandidateItem, type CollectResult } from '@/api/client'
import UrlInput from '@/components/UrlInput.vue'
import SavedResult from '@/components/SavedResult.vue'
import AggregationPreview from '@/components/AggregationPreview.vue'
import TimelineList from '@/components/TimelineList.vue'
import { Button } from '@/components/ui/button'

const router = useRouter()
const saved = ref<CollectResult | null>(null)
const candidates = ref<CandidateItem[] | null>(null)
const listKey = ref(0)

function onCollected(result: CollectResult) {
  saved.value = result
  listKey.value++
}

function onExtracted(list: CandidateItem[], _sourceUrl: string) {
  candidates.value = list
}

function onBatchSaved(n: number) {
  toast.success(`已收藏 ${n} 条`)
  candidates.value = null
  saved.value = null
  listKey.value++
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
    <header
      class="sticky top-0 z-10 space-y-3 border-b bg-background/85 px-4 py-3 backdrop-blur"
    >
      <div class="flex items-center justify-between">
        <h1 class="text-sm font-semibold">linkstash</h1>
        <Button variant="ghost" size="xs" @click="logout">退出</Button>
      </div>
      <UrlInput @collected="onCollected" />
    </header>

    <main class="space-y-4 px-4 py-4">
      <SavedResult
        v-if="saved"
        :result="saved"
        @extracted="onExtracted"
        @deleted="saved = null"
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
