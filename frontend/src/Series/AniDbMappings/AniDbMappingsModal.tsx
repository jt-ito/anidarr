import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import IconButton from 'Components/Link/IconButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons, kinds, sizes } from 'Helpers/Props';

export interface AniDbMappingResource {
  id: number;
  seriesId: number;
  aniDbId: number;
  seasonNumber: number;
  relationType: string;
}

export function useAniDbMappings(seriesId: number) {
  return useQuery<AniDbMappingResource[]>({
    queryKey: ['anidbmappings', seriesId],
    queryFn: async () => {
      const response = await fetch(`/api/v5/series/anidb-mapping?seriesId=${seriesId}`);
      if (!response.ok) throw new Error('Network response was not ok');
      return response.json();
    },
    enabled: !!seriesId,
  });
}

export function useCreateAniDbMapping() {
  const queryClient = useQueryClient();
  const { mutate: createMapping, isPending: isCreating } = useMutation({
    mutationFn: async (mapping: Partial<AniDbMappingResource>) => {
      const response = await fetch('/api/v5/series/anidb-mapping', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(mapping)
      });
      if (!response.ok) throw new Error('Network response was not ok');
      return response.json();
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['anidbmappings', variables.seriesId] });
      queryClient.invalidateQueries({ queryKey: ['series', variables.seriesId] });
    },
  });
  return { createMapping, isCreating };
}

export function useDeleteAniDbMapping() {
  const queryClient = useQueryClient();
  const { mutate: deleteMapping, isPending: isDeleting } = useMutation({
    mutationFn: async ({ seriesId, aniDbId }: { seriesId: number; aniDbId: number }) => {
      const response = await fetch(`/api/v5/series/anidb-mapping?seriesId=${seriesId}&aniDbId=${aniDbId}`, {
        method: 'DELETE'
      });
      if (!response.ok) throw new Error('Network response was not ok');
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['anidbmappings', variables.seriesId] });
      queryClient.invalidateQueries({ queryKey: ['series', variables.seriesId] });
    },
  });
  return { deleteMapping, isDeleting };
}

interface AniDbMappingsModalProps {
  isOpen: boolean;
  seriesId: number;
  onModalClose: () => void;
}

export default function AniDbMappingsModal({ isOpen, seriesId, onModalClose }: AniDbMappingsModalProps) {
  const { data: mappings, isFetching } = useAniDbMappings(seriesId);
  const { deleteMapping, isDeleting } = useDeleteAniDbMapping();
  const { createMapping, isCreating } = useCreateAniDbMapping();
  const [newAniDbId, setNewAniDbId] = useState('');
  const [newSeason, setNewSeason] = useState('');
  const [newRelation, setNewRelation] = useState('Manual');

  const handleDelete = (aniDbId: number) => {
    deleteMapping({ seriesId, aniDbId });
  };

  const handleAdd = () => {
    const id = parseInt(newAniDbId, 10);
    const season = parseInt(newSeason, 10);
    if (!isNaN(id) && !isNaN(season)) {
      createMapping({
        seriesId,
        aniDbId: id,
        seasonNumber: season,
        relationType: newRelation,
      });
      setNewAniDbId('');
      setNewSeason('');
    }
  };

  const inputStyle = { width: '100%', padding: '4px', boxSizing: 'border-box' as const };

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose} size={sizes.LARGE}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          AniDB Mappings
        </ModalHeader>
        <Alert kind={kinds.INFO}>
          This series is an AniDB hub. AniDB entries representing seasons or sequels are merged into this series.
        </Alert>

        {isFetching && !mappings ? (
          <LoadingIndicator />
        ) : (
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
                <th style={{ padding: '8px' }}>AniDB ID</th>
                <th style={{ padding: '8px' }}>Season</th>
                <th style={{ padding: '8px' }}>Relation</th>
                <th style={{ padding: '8px' }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {mappings?.map((m: AniDbMappingResource) => (
                <tr key={m.aniDbId} style={{ borderBottom: '1px solid #ccc' }}>
                  <td style={{ padding: '8px' }}>{m.aniDbId}</td>
                  <td style={{ padding: '8px' }}>{m.seasonNumber}</td>
                  <td style={{ padding: '8px' }}>{m.relationType}</td>
                  <td style={{ padding: '8px' }}>
                    <IconButton
                      name={icons.DELETE}
                      title="Unmerge"
                      onClick={() => handleDelete(m.aniDbId)}
                      disabled={isDeleting}
                    />
                  </td>
                </tr>
              ))}
              <tr>
                <td style={{ padding: '8px' }}>
                  <input
                    type="number"
                    value={newAniDbId}
                    onChange={(e) => setNewAniDbId(e.target.value)}
                    placeholder="AniDB ID"
                    style={inputStyle}
                  />
                </td>
                <td style={{ padding: '8px' }}>
                  <input
                    type="number"
                    value={newSeason}
                    onChange={(e) => setNewSeason(e.target.value)}
                    placeholder="Season"
                    style={inputStyle}
                  />
                </td>
                <td style={{ padding: '8px' }}>
                  <input
                    type="text"
                    value={newRelation}
                    onChange={(e) => setNewRelation(e.target.value)}
                    placeholder="Relation"
                    style={inputStyle}
                  />
                </td>
                <td style={{ padding: '8px' }}>
                  <Button onClick={handleAdd} disabled={isCreating || !newAniDbId || !newSeason}>
                    Add
                  </Button>
                </td>
              </tr>
            </tbody>
          </table>
        )}
      </ModalContent>
      <ModalFooter>
        <Button onClick={onModalClose}>Close</Button>
      </ModalFooter>
    </Modal>
  );
}
