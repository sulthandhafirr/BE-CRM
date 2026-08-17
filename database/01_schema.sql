--
-- PostgreSQL database dump
--

-- Dumped from database version 17.6
-- Dumped by pg_dump version 17.4

-- Started on 2026-08-17 14:07:04

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- TOC entry 136 (class 2615 OID 2200)
-- Name: public; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA public;


--
-- TOC entry 4011 (class 0 OID 0)
-- Dependencies: 136
-- Name: SCHEMA public; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON SCHEMA public IS 'standard public schema';


--
-- TOC entry 474 (class 1255 OID 85457)
-- Name: create_default_roles_for_company(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.create_default_roles_for_company() RETURNS trigger
    LANGUAGE plpgsql
    AS $$BEGIN
    INSERT INTO public.roles (role, company_id)
    VALUES
        ('admin', NEW.id),
        ('customer', NEW.id),
        ('technician', NEW.id),
        ('cs_agent', NEW.id);

    RETURN NEW;
END;$$;


--
-- TOC entry 449 (class 1255 OID 54233)
-- Name: generate_ticket_id(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.generate_ticket_id() RETURNS trigger
    LANGUAGE plpgsql
    AS $$DECLARE
  today TEXT;
  seq INT;
  new_id BIGINT;
  company_id_val INT;
  company_prefix TEXT;
BEGIN
  today := TO_CHAR(NEW.created_at, 'YYMMDD');

  -- get company_id from the customer profile
  SELECT p.company_id INTO company_id_val
  FROM profile p
  WHERE p.id = NEW.customer_id;

  -- company_id to 2 digits e.g. '01'
  -- company_prefix := LPAD(company_id_val::TEXT, 2, '0');
  company_prefix := company_id_val::TEXT;

  -- serialize concurrent inserts for the same company+day
  PERFORM pg_advisory_xact_lock(hashtext(company_prefix || today));

  -- count tickets from same company today
  SELECT COUNT(*) + 1 INTO seq
  FROM ticket t
  JOIN profile p ON t.customer_id = p.id
  WHERE t.id::TEXT LIKE (company_prefix || today) || '%'
  AND p.company_id = company_id_val;

  new_id := (company_prefix || today || LPAD(seq::TEXT, 4, '0'))::BIGINT;

  NEW.id := new_id;
  RETURN NEW;
END;$$;


--
-- TOC entry 442 (class 1255 OID 59311)
-- Name: get_my_role(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.get_my_role() RETURNS text
    LANGUAGE sql STABLE SECURITY DEFINER
    AS $$
  SELECT r.role
  FROM public.profile p
  JOIN public.roles r ON p.role_id = r.id
  WHERE p.id = auth.uid()
$$;


--
-- TOC entry 483 (class 1255 OID 80697)
-- Name: get_tickets_by_company(integer, timestamp with time zone, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.get_tickets_by_company(p_company_id integer, p_start_date timestamp with time zone DEFAULT NULL::timestamp with time zone, p_end_date timestamp with time zone DEFAULT NULL::timestamp with time zone) RETURNS TABLE(id bigint, subject text, description text, status text, first_response_at timestamp with time zone, resolved_at timestamp with time zone, created_at timestamp with time zone, customer_name text, agent_name text, technician_name text, priority_label text)
    LANGUAGE sql SECURITY DEFINER
    AS $$
  SELECT 
    t.id, t.subject, t.description, t.status,
    t.first_response_at, t.resolved_at, t.created_at,
    cust.name    AS customer_name,
    ag.name      AS agent_name,
    tech.name    AS technician_name,
    pr.priority  AS priority_label
  FROM ticket t
  INNER JOIN profile cust ON cust.id = t.customer_id
  LEFT  JOIN profile ag   ON ag.id   = t.agent_id
  LEFT  JOIN profile tech ON tech.id = t.technician_id
  LEFT  JOIN priority pr  ON pr.id   = t.user_choosen_priority_id -- Berhasil diperbaiki di sini
  WHERE cust.company_id = p_company_id
    AND (p_start_date IS NULL OR t.created_at >= p_start_date)
    AND (p_end_date IS NULL OR t.created_at <= p_end_date)
  ORDER BY t.created_at DESC;
$$;


--
-- TOC entry 478 (class 1255 OID 83528)
-- Name: seed_default_tiers(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.seed_default_tiers() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    INSERT INTO public.tiers (tier, company_id, color, level)
    VALUES
        ('Bronze', NEW.id, '#b45309', 1),
        ('Silver', NEW.id, '#6b7280', 2),
        ('Gold', NEW.id, '#ca8a04', 3);
    RETURN NEW;
END;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- TOC entry 405 (class 1259 OID 79582)
-- Name: company; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.company (
    id bigint NOT NULL,
    company_name character varying NOT NULL,
    company_code character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    support_email text,
    phone_number text,
    timezone text DEFAULT 'Asia/Jakarta'::text,
    working_days text[] DEFAULT ARRAY['Mon'::text, 'Tue'::text, 'Wed'::text, 'Thu'::text, 'Fri'::text],
    working_hours_start text DEFAULT '09:00'::text,
    working_hours_end text DEFAULT '18:00'::text,
    logo_url text,
    ticket_status_config jsonb DEFAULT '{}'::jsonb NOT NULL,
    sla_config jsonb DEFAULT '{}'::jsonb NOT NULL,
    export_schedule_config jsonb DEFAULT '{}'::jsonb,
    subscription_plan text,
    subscription_status text,
    subscription_end timestamp with time zone,
    trial_use boolean DEFAULT false NOT NULL,
    cancel_at_period_end boolean DEFAULT false NOT NULL,
    CONSTRAINT company_subscription_plan_check CHECK (((subscription_plan = ANY (ARRAY['trial'::text, 'monthly'::text, 'yearly'::text])) OR (subscription_plan IS NULL))),
    CONSTRAINT company_subscription_status_check CHECK (((subscription_status = ANY (ARRAY['trial'::text, 'pending'::text, 'active'::text, 'expired'::text])) OR (subscription_status IS NULL)))
);


--
-- TOC entry 4012 (class 0 OID 0)
-- Dependencies: 405
-- Name: TABLE company; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.company IS 'Company table';


--
-- TOC entry 406 (class 1259 OID 79585)
-- Name: company_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.company ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.company_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 412 (class 1259 OID 81939)
-- Name: intent; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.intent (
    id bigint NOT NULL,
    intent text
);


--
-- TOC entry 407 (class 1259 OID 80066)
-- Name: notification; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.notification (
    id bigint NOT NULL,
    user_id uuid NOT NULL,
    message character varying NOT NULL,
    is_read boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- TOC entry 4013 (class 0 OID 0)
-- Dependencies: 407
-- Name: TABLE notification; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.notification IS 'Web Notification Storage';


--
-- TOC entry 408 (class 1259 OID 80069)
-- Name: notification_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.notification ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.notification_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 418 (class 1259 OID 85055)
-- Name: payment; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment (
    id bigint NOT NULL,
    ticket_id bigint NOT NULL,
    amount numeric(12,2) NOT NULL,
    status text DEFAULT 'pending'::text NOT NULL,
    midtrans_order_id text NOT NULL,
    midtrans_transaction_id text,
    payment_method text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    paid_at timestamp with time zone,
    due_date timestamp with time zone,
    snap_token text
);


--
-- TOC entry 417 (class 1259 OID 85054)
-- Name: payment_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.payment_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- TOC entry 4014 (class 0 OID 0)
-- Dependencies: 417
-- Name: payment_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.payment_id_seq OWNED BY public.payment.id;


--
-- TOC entry 393 (class 1259 OID 53007)
-- Name: priority; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.priority (
    id bigint NOT NULL,
    priority text NOT NULL
);


--
-- TOC entry 4015 (class 0 OID 0)
-- Dependencies: 393
-- Name: TABLE priority; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.priority IS 'List of Priority';


--
-- TOC entry 394 (class 1259 OID 53010)
-- Name: priority_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.priority ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.priority_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 389 (class 1259 OID 46152)
-- Name: profile; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.profile (
    id uuid NOT NULL,
    email text NOT NULL,
    name text NOT NULL,
    role_id bigint,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    "Position" text,
    company_id bigint,
    avatar_url text
);


--
-- TOC entry 4016 (class 0 OID 0)
-- Dependencies: 389
-- Name: TABLE profile; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.profile IS 'Profile Account Table';


--
-- TOC entry 404 (class 1259 OID 79245)
-- Name: profile_skill; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.profile_skill (
    profile_id uuid,
    skill_id bigint
);


--
-- TOC entry 411 (class 1259 OID 81528)
-- Name: profile_tier; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.profile_tier (
    profile_id uuid NOT NULL,
    tier_id bigint NOT NULL,
    id bigint NOT NULL
);


--
-- TOC entry 415 (class 1259 OID 83505)
-- Name: profile_tier_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.profile_tier ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.profile_tier_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 414 (class 1259 OID 83336)
-- Name: role_permissions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.role_permissions (
    id bigint NOT NULL,
    role_id integer NOT NULL,
    permissions jsonb DEFAULT '{}'::jsonb NOT NULL
);


--
-- TOC entry 413 (class 1259 OID 83335)
-- Name: role_permissions_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.role_permissions ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.role_permissions_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 390 (class 1259 OID 46207)
-- Name: roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.roles (
    id bigint NOT NULL,
    role text NOT NULL,
    company_id integer NOT NULL,
    is_system boolean DEFAULT false NOT NULL
);


--
-- TOC entry 4017 (class 0 OID 0)
-- Dependencies: 390
-- Name: TABLE roles; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.roles IS 'Roles Table';


--
-- TOC entry 391 (class 1259 OID 46210)
-- Name: roles_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.roles ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.roles_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 395 (class 1259 OID 53028)
-- Name: skills; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.skills (
    id bigint NOT NULL,
    skill text NOT NULL,
    company_id bigint
);


--
-- TOC entry 396 (class 1259 OID 53031)
-- Name: skills_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.skills ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.skills_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 426 (class 1259 OID 85626)
-- Name: subscription_payment; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.subscription_payment (
    id bigint NOT NULL,
    company_id bigint NOT NULL,
    subscription_plan text NOT NULL,
    amount numeric(18,2) NOT NULL,
    status text DEFAULT 'pending'::text NOT NULL,
    midtrans_order_id text NOT NULL,
    midtrans_transaction_id text,
    payment_method text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    paid_at timestamp with time zone,
    subscription_start timestamp with time zone,
    subscription_end timestamp with time zone
);


--
-- TOC entry 425 (class 1259 OID 85625)
-- Name: subscription_payment_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.subscription_payment ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.subscription_payment_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 397 (class 1259 OID 54196)
-- Name: ticket; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ticket (
    id bigint NOT NULL,
    customer_id uuid NOT NULL,
    subject character varying NOT NULL,
    description text NOT NULL,
    priority_id bigint,
    status character varying DEFAULT 'Waiting'::character varying NOT NULL,
    sla_deadline timestamp with time zone,
    first_response_at timestamp with time zone,
    resolved_at timestamp with time zone,
    sla_breached boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone NOT NULL,
    agent_id uuid,
    technician_id uuid,
    response_time_sec integer,
    resolution_time_sec integer,
    user_choosen_priority_id bigint,
    intent text,
    intent_confidence double precision,
    intent_id bigint,
    urgency text,
    urgency_confidence double precision,
    sla_reminder_sent boolean DEFAULT false NOT NULL,
    sla_breached_notified boolean DEFAULT false NOT NULL,
    is_billable boolean DEFAULT false NOT NULL,
    bill_amount numeric(12,2),
    bill_items jsonb,
    bill_sent boolean DEFAULT false NOT NULL
);


--
-- TOC entry 4018 (class 0 OID 0)
-- Dependencies: 397
-- Name: TABLE ticket; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.ticket IS 'Store Incoming Ticket';


--
-- TOC entry 4019 (class 0 OID 0)
-- Dependencies: 397
-- Name: COLUMN ticket.technician_id; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.ticket.technician_id IS 'Dispatched Technician to a ticket(s)';


--
-- TOC entry 398 (class 1259 OID 55406)
-- Name: ticket_attachment; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ticket_attachment (
    id bigint NOT NULL,
    ticket_id bigint NOT NULL,
    file_url text,
    file_name character varying,
    file_size bigint,
    uploaded_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- TOC entry 4020 (class 0 OID 0)
-- Dependencies: 398
-- Name: TABLE ticket_attachment; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.ticket_attachment IS 'Ticket''s File Attachment';


--
-- TOC entry 399 (class 1259 OID 55409)
-- Name: ticket_attachment_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.ticket_attachment ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.ticket_attachment_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 402 (class 1259 OID 69431)
-- Name: ticket_comment; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ticket_comment (
    id bigint NOT NULL,
    ticket_id bigint NOT NULL,
    sender_id uuid,
    message text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- TOC entry 4021 (class 0 OID 0)
-- Dependencies: 402
-- Name: TABLE ticket_comment; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.ticket_comment IS 'Ticket''s comment section';


--
-- TOC entry 403 (class 1259 OID 69434)
-- Name: ticket_comment_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.ticket_comment ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.ticket_comment_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 416 (class 1259 OID 85032)
-- Name: ticket_rating; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ticket_rating (
    id uuid NOT NULL,
    ticket_id bigint NOT NULL,
    rate bigint NOT NULL,
    rated_at timestamp with time zone NOT NULL,
    message text
);


--
-- TOC entry 409 (class 1259 OID 81509)
-- Name: tiers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.tiers (
    id bigint NOT NULL,
    tier text NOT NULL,
    company_id bigint NOT NULL,
    color character varying(20) DEFAULT '#6b7280'::character varying NOT NULL,
    level bigint NOT NULL
);


--
-- TOC entry 410 (class 1259 OID 81512)
-- Name: tiers_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.tiers ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.tiers_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 3745 (class 2604 OID 85058)
-- Name: payment id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment ALTER COLUMN id SET DEFAULT nextval('public.payment_id_seq'::regclass);


--
-- TOC entry 3777 (class 2606 OID 79593)
-- Name: company company_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.company
    ADD CONSTRAINT company_pkey PRIMARY KEY (id);


--
-- TOC entry 3786 (class 2606 OID 83107)
-- Name: intent intent_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.intent
    ADD CONSTRAINT intent_pkey PRIMARY KEY (id);


--
-- TOC entry 3779 (class 2606 OID 80078)
-- Name: notification notification_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification
    ADD CONSTRAINT notification_pkey PRIMARY KEY (id);


--
-- TOC entry 3794 (class 2606 OID 85066)
-- Name: payment payment_midtrans_order_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment
    ADD CONSTRAINT payment_midtrans_order_id_key UNIQUE (midtrans_order_id);


--
-- TOC entry 3796 (class 2606 OID 85064)
-- Name: payment payment_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment
    ADD CONSTRAINT payment_pkey PRIMARY KEY (id);


--
-- TOC entry 3764 (class 2606 OID 53017)
-- Name: priority priority_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.priority
    ADD CONSTRAINT priority_pkey PRIMARY KEY (id);


--
-- TOC entry 3754 (class 2606 OID 46156)
-- Name: profile profile_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.profile
    ADD CONSTRAINT profile_id_key UNIQUE (id);


--
-- TOC entry 3756 (class 2606 OID 46161)
-- Name: profile profile_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.profile
    ADD CONSTRAINT profile_pkey PRIMARY KEY (id);


--
-- TOC entry 3784 (class 2606 OID 83510)
-- Name: profile_tier profile_tier_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.profile_tier
    ADD CONSTRAINT profile_tier_pkey PRIMARY KEY (id);


--
-- TOC entry 3788 (class 2606 OID 83343)
-- Name: role_permissions role_permissions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_pkey PRIMARY KEY (id);


--
-- TOC entry 3790 (class 2606 OID 83345)
-- Name: role_permissions role_permissions_role_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_role_id_key UNIQUE (role_id);


--
-- TOC entry 3758 (class 2606 OID 46212)
-- Name: roles roles_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_id_key UNIQUE (id);


--
-- TOC entry 3760 (class 2606 OID 46222)
-- Name: roles roles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (id);


--
-- TOC entry 3762 (class 2606 OID 83334)
-- Name: roles roles_role_company_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_role_company_id_key UNIQUE (role, company_id);


--
-- TOC entry 3766 (class 2606 OID 53038)
-- Name: skills skills_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.skills
    ADD CONSTRAINT skills_pkey PRIMARY KEY (id);


--
-- TOC entry 3803 (class 2606 OID 85634)
-- Name: subscription_payment subscription_payment_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_payment
    ADD CONSTRAINT subscription_payment_pkey PRIMARY KEY (id);


--
-- TOC entry 3772 (class 2606 OID 55417)
-- Name: ticket_attachment ticket_attachment_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_attachment
    ADD CONSTRAINT ticket_attachment_pkey PRIMARY KEY (id);


--
-- TOC entry 3774 (class 2606 OID 69442)
-- Name: ticket_comment ticket_comment_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_comment
    ADD CONSTRAINT ticket_comment_pkey PRIMARY KEY (id);


--
-- TOC entry 3768 (class 2606 OID 54200)
-- Name: ticket ticket_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket
    ADD CONSTRAINT ticket_id_key UNIQUE (id);


--
-- TOC entry 3770 (class 2606 OID 54215)
-- Name: ticket ticket_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket
    ADD CONSTRAINT ticket_pkey PRIMARY KEY (id);


--
-- TOC entry 3792 (class 2606 OID 85236)
-- Name: ticket_rating ticket_rating_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_rating
    ADD CONSTRAINT ticket_rating_pkey PRIMARY KEY (id);


--
-- TOC entry 3781 (class 2606 OID 81519)
-- Name: tiers tiers_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tiers
    ADD CONSTRAINT tiers_pkey PRIMARY KEY (id);


--
-- TOC entry 3775 (class 1259 OID 85497)
-- Name: company_company_code_unique; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX company_company_code_unique ON public.company USING btree (lower((company_code)::text));


--
-- TOC entry 3752 (class 1259 OID 85498)
-- Name: profile_email_unique; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX profile_email_unique ON public.profile USING btree (lower(email));


--
-- TOC entry 3798 (class 1259 OID 85641)
-- Name: subscription_payment_company_id_created_at_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX subscription_payment_company_id_created_at_idx ON public.subscription_payment USING btree (company_id, created_at DESC);


--
-- TOC entry 3799 (class 1259 OID 85642)
-- Name: subscription_payment_company_id_status_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX subscription_payment_company_id_status_idx ON public.subscription_payment USING btree (company_id, status);


--
-- TOC entry 3800 (class 1259 OID 85643)
-- Name: subscription_payment_company_id_subscription_end_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX subscription_payment_company_id_subscription_end_idx ON public.subscription_payment USING btree (company_id, subscription_end);


--
-- TOC entry 3801 (class 1259 OID 85640)
-- Name: subscription_payment_midtrans_order_id_unique; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX subscription_payment_midtrans_order_id_unique ON public.subscription_payment USING btree (midtrans_order_id);


--
-- TOC entry 3797 (class 1259 OID 85074)
-- Name: ux_payment_ticket_pending; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_payment_ticket_pending ON public.payment USING btree (ticket_id) WHERE (status = 'pending'::text);


--
-- TOC entry 3782 (class 1259 OID 83525)
-- Name: ux_tiers_company_tiername; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_tiers_company_tiername ON public.tiers USING btree (company_id, lower(tier));


--
-- TOC entry 3828 (class 2620 OID 54234)
-- Name: ticket set_ticket_id; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER set_ticket_id BEFORE INSERT ON public.ticket FOR EACH ROW EXECUTE FUNCTION public.generate_ticket_id();


--
-- TOC entry 3829 (class 2620 OID 85466)
-- Name: company trg_seed_default_roles; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_seed_default_roles AFTER INSERT ON public.company FOR EACH ROW EXECUTE FUNCTION public.create_default_roles_for_company();


--
-- TOC entry 3830 (class 2620 OID 83529)
-- Name: company trg_seed_default_tiers; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_seed_default_tiers AFTER INSERT ON public.company FOR EACH ROW EXECUTE FUNCTION public.seed_default_tiers();


--
-- TOC entry 3820 (class 2606 OID 80079)
-- Name: notification notification_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification
    ADD CONSTRAINT notification_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.profile(id) ON UPDATE CASCADE;


--
-- TOC entry 3826 (class 2606 OID 85067)
-- Name: payment payment_ticket_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment
    ADD CONSTRAINT payment_ticket_id_fkey FOREIGN KEY (ticket_id) REFERENCES public.ticket(id);


--
-- TOC entry 3804 (class 2606 OID 79629)
-- Name: profile profile_company_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.profile
    ADD CONSTRAINT profile_company_id_fkey FOREIGN KEY (company_id) REFERENCES public.company(id) ON UPDATE CASCADE;


--
-- TOC entry 3805 (class 2606 OID 46162)
-- Name: profile profile_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.profile
    ADD CONSTRAINT profile_id_fkey FOREIGN KEY (id) REFERENCES auth.users(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- TOC entry 3806 (class 2606 OID 83368)
-- Name: profile profile_role_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.profile
    ADD CONSTRAINT profile_role_id_fkey FOREIGN KEY (role_id) REFERENCES public.roles(id) ON UPDATE CASCADE;


--
-- TOC entry 3818 (class 2606 OID 79250)
-- Name: profile_skill profile_skill_profile_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.profile_skill
    ADD CONSTRAINT profile_skill_profile_id_fkey FOREIGN KEY (profile_id) REFERENCES public.profile(id);


--
-- TOC entry 3819 (class 2606 OID 79255)
-- Name: profile_skill profile_skill_skill_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.profile_skill
    ADD CONSTRAINT profile_skill_skill_id_fkey FOREIGN KEY (skill_id) REFERENCES public.skills(id);


--
-- TOC entry 3822 (class 2606 OID 81531)
-- Name: profile_tier profile_tier_profile_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.profile_tier
    ADD CONSTRAINT profile_tier_profile_id_fkey FOREIGN KEY (profile_id) REFERENCES public.profile(id);


--
-- TOC entry 3823 (class 2606 OID 81536)
-- Name: profile_tier profile_tier_tier_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.profile_tier
    ADD CONSTRAINT profile_tier_tier_id_fkey FOREIGN KEY (tier_id) REFERENCES public.tiers(id);


--
-- TOC entry 3824 (class 2606 OID 83346)
-- Name: role_permissions role_permissions_role_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_role_id_fkey FOREIGN KEY (role_id) REFERENCES public.roles(id) ON DELETE CASCADE;


--
-- TOC entry 3807 (class 2606 OID 83328)
-- Name: roles roles_company_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_company_id_fkey FOREIGN KEY (company_id) REFERENCES public.company(id) ON DELETE CASCADE;


--
-- TOC entry 3808 (class 2606 OID 84585)
-- Name: skills skills_company_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.skills
    ADD CONSTRAINT skills_company_id_fkey FOREIGN KEY (company_id) REFERENCES public.company(id);


--
-- TOC entry 3827 (class 2606 OID 85635)
-- Name: subscription_payment subscription_payment_company_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_payment
    ADD CONSTRAINT subscription_payment_company_id_fkey FOREIGN KEY (company_id) REFERENCES public.company(id) ON UPDATE CASCADE ON DELETE RESTRICT;


--
-- TOC entry 3809 (class 2606 OID 54226)
-- Name: ticket ticket_agent_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket
    ADD CONSTRAINT ticket_agent_id_fkey FOREIGN KEY (agent_id) REFERENCES public.profile(id) ON UPDATE CASCADE;


--
-- TOC entry 3815 (class 2606 OID 75631)
-- Name: ticket_attachment ticket_attachment_ticket_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_attachment
    ADD CONSTRAINT ticket_attachment_ticket_id_fkey FOREIGN KEY (ticket_id) REFERENCES public.ticket(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- TOC entry 3816 (class 2606 OID 75640)
-- Name: ticket_comment ticket_comment_ticket_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_comment
    ADD CONSTRAINT ticket_comment_ticket_id_fkey FOREIGN KEY (ticket_id) REFERENCES public.ticket(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- TOC entry 3817 (class 2606 OID 69448)
-- Name: ticket_comment ticket_comment_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_comment
    ADD CONSTRAINT ticket_comment_user_id_fkey FOREIGN KEY (sender_id) REFERENCES public.profile(id) ON UPDATE CASCADE ON DELETE SET NULL;


--
-- TOC entry 3810 (class 2606 OID 54221)
-- Name: ticket ticket_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket
    ADD CONSTRAINT ticket_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.profile(id) ON UPDATE CASCADE;


--
-- TOC entry 3811 (class 2606 OID 83118)
-- Name: ticket ticket_intent_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket
    ADD CONSTRAINT ticket_intent_id_fkey FOREIGN KEY (intent_id) REFERENCES public.intent(id) ON UPDATE CASCADE;


--
-- TOC entry 3812 (class 2606 OID 54216)
-- Name: ticket ticket_priority_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket
    ADD CONSTRAINT ticket_priority_id_fkey FOREIGN KEY (priority_id) REFERENCES public.priority(id) ON UPDATE CASCADE;


--
-- TOC entry 3825 (class 2606 OID 85238)
-- Name: ticket_rating ticket_rating_ticket_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_rating
    ADD CONSTRAINT ticket_rating_ticket_id_fkey FOREIGN KEY (ticket_id) REFERENCES public.ticket(id);


--
-- TOC entry 3813 (class 2606 OID 76855)
-- Name: ticket ticket_technician_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket
    ADD CONSTRAINT ticket_technician_id_fkey FOREIGN KEY (technician_id) REFERENCES public.profile(id) ON UPDATE CASCADE;


--
-- TOC entry 3814 (class 2606 OID 79829)
-- Name: ticket ticket_user_choosen_priority_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket
    ADD CONSTRAINT ticket_user_choosen_priority_id_fkey FOREIGN KEY (user_choosen_priority_id) REFERENCES public.priority(id) ON UPDATE CASCADE;


--
-- TOC entry 3821 (class 2606 OID 83484)
-- Name: tiers tiers_company_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tiers
    ADD CONSTRAINT tiers_company_id_fkey FOREIGN KEY (company_id) REFERENCES public.company(id);


--
-- TOC entry 3996 (class 3256 OID 79287)
-- Name: profile Admin can view all table; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY "Admin can view all table" ON public.profile FOR SELECT USING (((auth.uid() = id) OR (public.get_my_role() = 'admin'::text)));


--
-- TOC entry 4000 (class 3256 OID 80264)
-- Name: ticket_comment Enable read access for all authenticated users; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY "Enable read access for all authenticated users" ON public.ticket_comment FOR SELECT TO authenticated USING (true);


--
-- TOC entry 3998 (class 3256 OID 46283)
-- Name: profile Users can read own profile; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY "Users can read own profile" ON public.profile FOR SELECT USING ((auth.uid() = id));


--
-- TOC entry 3999 (class 3256 OID 46284)
-- Name: roles Users can read own roles; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY "Users can read own roles" ON public.roles FOR SELECT USING (true);


--
-- TOC entry 4001 (class 3256 OID 80099)
-- Name: notification Users can view own notifications; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY "Users can view own notifications" ON public.notification FOR SELECT USING ((auth.uid() = user_id));


--
-- TOC entry 3987 (class 0 OID 79582)
-- Dependencies: 405
-- Name: company; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.company ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3991 (class 0 OID 81939)
-- Dependencies: 412
-- Name: intent; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.intent ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3988 (class 0 OID 80066)
-- Dependencies: 407
-- Name: notification; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.notification ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3994 (class 0 OID 85055)
-- Dependencies: 418
-- Name: payment; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3981 (class 0 OID 53007)
-- Dependencies: 393
-- Name: priority; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.priority ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3979 (class 0 OID 46152)
-- Dependencies: 389
-- Name: profile; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.profile ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3986 (class 0 OID 79245)
-- Dependencies: 404
-- Name: profile_skill; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.profile_skill ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3990 (class 0 OID 81528)
-- Dependencies: 411
-- Name: profile_tier; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.profile_tier ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3992 (class 0 OID 83336)
-- Dependencies: 414
-- Name: role_permissions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.role_permissions ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3980 (class 0 OID 46207)
-- Dependencies: 390
-- Name: roles; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.roles ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3982 (class 0 OID 53028)
-- Dependencies: 395
-- Name: skills; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.skills ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3995 (class 0 OID 85626)
-- Dependencies: 426
-- Name: subscription_payment; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.subscription_payment ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3983 (class 0 OID 54196)
-- Dependencies: 397
-- Name: ticket; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.ticket ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3984 (class 0 OID 55406)
-- Dependencies: 398
-- Name: ticket_attachment; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.ticket_attachment ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3985 (class 0 OID 69431)
-- Dependencies: 402
-- Name: ticket_comment; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.ticket_comment ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3993 (class 0 OID 85032)
-- Dependencies: 416
-- Name: ticket_rating; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.ticket_rating ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3989 (class 0 OID 81509)
-- Dependencies: 409
-- Name: tiers; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.tiers ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 3997 (class 3256 OID 59312)
-- Name: profile ultrauser can view all profiles; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY "ultrauser can view all profiles" ON public.profile FOR SELECT USING (((auth.uid() = id) OR (public.get_my_role() = 'ultrauser'::text)));


-- Completed on 2026-08-17 14:07:08

--
-- PostgreSQL database dump complete
--

