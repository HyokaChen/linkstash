<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { api, type CandidateItem, type CollectResult } from '@/api/client'
import UrlInput from '@/components/UrlInput.vue'
import LinkCard from '@/components/LinkCard.vue'
import AggregationPreview from '@/components/AggregationPreview.vue'
import CollectionList from '@/components/CollectionList.vue'
import { Button } from '@/components/ui/button'

const router = useRouter()
const single = ref<CollectResult | null>(null)
const candidates = ref<CandidateItem[] | null>(null)
const collectionKey = ref(0)

function onCollected(result: CollectResult) {
  single.value = result
  collectionKey.value++
}

function onExtracted(list: CandidateItem[]) {
  candidates.value = list
}

function onBatchSaved(n: number) {
  toast.success(`已收藏 ${n} 条`)
  candidates.value = null
  single.value = null
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
    <UrlInput @collected="onCollected" @extracted="onExtracted" />
    <LinkCard v-if="single" :item="single" @deleted="single = null" />
    <AggregationPreview
      v-if="candidates"
      :candidates="candidates"
      @saved="onBatchSaved"
      @close="candidates = null"
    />
    <CollectionList :key="collectionKey" />
  </div>
</template>