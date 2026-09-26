<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { api, type CandidateItem, type CollectResult } from '@/api/client'
import UrlInput from '@/components/UrlInput.vue'
import SavedResult from '@/components/SavedResult.vue'
import AggregationPreview from '@/components/AggregationPreview.vue'
import CollectionList from '@/components/CollectionList.vue'
import { Button } from '@/components/ui/button'

const router = useRouter()
const saved = ref<CollectResult | null>(null)
const candidates = ref<CandidateItem[] | null>(null)
const collectionKey = ref(0)

function onCollected(result: CollectResult) {
  saved.value = result
  collectionKey.value++
}

/** 展开聚合条目：复用已收藏结果区的上下文，来源页仅用于日志/回溯。 */
function onExtracted(list: CandidateItem[], _sourceUrl: string) {
  candidates.value = list
}

function onBatchSaved(n: number) {
  toast.success(`已收藏 ${n} 条`)
  candidates.value = null
  collectionKey.value++
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
  <div class="mx-auto max-w-3xl space-y-6 px-4 py-6">
    <div class="flex items-center justify-between">
      <h1 class="text-xl font-semibold">linkstash</h1>
      <Button variant="ghost" size="sm" @click="logout">退出</Button>
    </div>

    <UrlInput @collected="onCollected" />

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

    <CollectionList :key="collectionKey" />
  </div>
</template>
