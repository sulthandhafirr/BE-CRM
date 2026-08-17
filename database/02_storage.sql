INSERT INTO storage.buckets (
    id,
    name,
    public,
    file_size_limit,
    allowed_mime_types
)
VALUES (
    'ticket-attachment',
    'ticket-attachment',
    false,
    10485760,
    ARRAY[
        'image/jpeg',
        'image/png',
        'application/pdf',
        'application/msword',
        'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        'application/vnd.ms-excel',
        'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        'text/csv'
    ]
)

ON CONFLICT (id) DO NOTHING;

INSERT INTO storage.buckets (
    id,
    name,
    public,
    file_size_limit,
    allowed_mime_types
)
VALUES (
    'avatars',
    'avatars',
    true,
    5242880,
    ARRAY[
        'image/jpeg',
        'image/png',
        'image/webp'
    ]
)
ON CONFLICT (id) DO NOTHING;

INSERT INTO storage.buckets (
    id,
    name,
    public
)
VALUES (
    'company-logos',
    'company-logos',
    true
)
ON CONFLICT (id) DO NOTHING;

-- Avatars bucket policies

CREATE POLICY "Avatar images are publicly viewable"
ON storage.objects FOR SELECT
TO public
USING (bucket_id = 'avatars');

CREATE POLICY "Users can upload their own avatar"
ON storage.objects FOR INSERT
TO authenticated
WITH CHECK (
    bucket_id = 'avatars'
    AND (storage.foldername(name))[1] = auth.uid()::text
);

CREATE POLICY "Users can update their own avatar"
ON storage.objects FOR UPDATE
TO authenticated
USING (
    bucket_id = 'avatars'
    AND (storage.foldername(name))[1] = auth.uid()::text
);

CREATE POLICY "Users can delete their own avatar"
ON storage.objects FOR DELETE
TO authenticated
USING (
    bucket_id = 'avatars'
    AND (storage.foldername(name))[1] = auth.uid()::text
);