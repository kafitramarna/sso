export interface UserDto {
  id: string;
  email: string;
  display_name: string | null;
  email_verified: boolean;
  is_sso_admin: boolean;
  status: string;
  last_login_at: string | null;
  created_at: string;
}

export interface AuthPayload {
  token: string;
  expires_in: number;
  user: UserDto;
}

export interface SessionDto {
  family_id: string;
  user_agent: string | null;
  ip: string | null;
  created_at: string;
  last_seen_at: string;
  current: boolean;
}

export interface ClientDto {
  id: string;
  client_id: string;
  name: string;
  type: "confidential" | "public";
  active: boolean;
  redirect_uris: string[];
  allowed_scopes: string[];
  skip_consent: boolean;
  launch_url: string | null;
  description: string | null;
  require_assignment: boolean;
  created_at: string;
}

/** Launchable app on /dashboard — only clients with a launch_url appear here. */
export interface AppDto {
  client_id: string;
  name: string;
  description: string | null;
  launch_url: string | null;
}

export interface InviteResult {
  email: string;
  invite_url: string;
  expires_at: string;
}

export interface Envelope<T> {
  success: boolean;
  data: T;
  error?: { code: string; message: string; details?: unknown } | null;
  meta?: { request_id?: string; timestamp?: string };
}
