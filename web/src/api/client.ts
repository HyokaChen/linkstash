export interface CandidateItem {
  url: string
  title: string
  translation: string
  isFallbackTitle: boolean
}

export interface CollectionItem {
  id: string
  url: string
  title: string
  translation: string
  sourceUrl: string | null
  isFallbackTitle: boolean
  createdAt: string
  tags: string
  groupId: string | null
}

export interface CollectResult extends CollectionItem {
  markdown: string
  itemCount: number
}

/** 统一解析结果条目。isExisting=true 表示已收藏，前端置灰不可勾选。 */
export interface ResolveCandidate {
  input: string
  url: string
  title: string
  translation: string
  isFallbackTitle: boolean
  tags: string
  isExisting: boolean
}

export interface ResolveResult {
  resolved: ResolveCandidate[]
  unresolved: string[]
}

export interface ImportCandidate {
  url: string
  title: string
  folderPath: string
  isExisting: boolean
}

export interface ImportPreviewResult {
  groupId: string
  name: string
  total: number
  skipped: number
  candidates: ImportCandidate[]
}

export interface BatchItemPayload {
  url: string
  title: string
  translation: string
  groupId?: string
}

export interface CollectionsPage {
  items: CollectionItem[]
  total: number
  page: number
  pageSize: number
}

export async function api<T = unknown>(path: string, opts: RequestInit = {}): Promise<T> {
  const resp = await fetch(path, {
    ...opts,
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...(opts.headers ?? {}) },
  })
  if (resp.status === 401 && !path.startsWith('/api/auth/login')) {
    window.location.href = '/login'
    throw Object.assign(new Error('unauthorized'), { status: 401 })
  }
  if (!resp.ok) {
    throw Object.assign(new Error(await resp.text().catch(() => resp.statusText)), { status: resp.status })
  }
  return resp.json() as Promise<T>
}