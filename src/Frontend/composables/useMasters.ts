/**
 * 17 マスタ共通の API CRUD ラッパー。
 * Backend の /api/v1/masters/{slug} エンドポイントを叩く。
 */

export interface MasterItem {
  id: number
  code: string
  name: string
  deleteFlag: boolean
  createdAt: string
  updatedAt: string
  // 拡張カラムは Record で動的に保持
  [key: string]: unknown
}

export const useMasters = () => {
  const { apiFetch } = useApi()

  const list = async (slug: string, includeDeleted = false): Promise<MasterItem[]> => {
    const res = await apiFetch<{ data: MasterItem[] }>(
      `/masters/${slug}?includeDeleted=${includeDeleted}`,
    )
    return res.data
  }

  const create = async (slug: string, payload: Record<string, unknown>): Promise<MasterItem> => {
    return await apiFetch<MasterItem>(`/masters/${slug}`, {
      method: 'POST',
      body: payload,
    })
  }

  const update = async (
    slug: string,
    id: number,
    payload: Record<string, unknown>,
  ): Promise<MasterItem> => {
    return await apiFetch<MasterItem>(`/masters/${slug}/${id}`, {
      method: 'PATCH',
      body: payload,
    })
  }

  const softDelete = async (slug: string, id: number): Promise<void> => {
    await apiFetch<void>(`/masters/${slug}/${id}`, { method: 'DELETE' })
  }

  const restore = async (slug: string, id: number): Promise<void> => {
    await apiFetch<void>(`/masters/${slug}/${id}/restore`, { method: 'POST' })
  }

  return { list, create, update, softDelete, restore }
}
