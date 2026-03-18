/**
 * rpc_globals.h — Application-wide shared service record handles
 *
 * Opened once at startup in flipper_zero_rpc_daemon.c, closed on exit.
 * Handler modules that need Storage or Notification include this header.
 */

#pragma once

#include <storage/storage.h>
#include <notification/notification.h>
#include <notification/notification_messages.h>

/** Storage service handle — valid for the lifetime of the application. */
extern Storage* g_storage;

/** Notification service handle — valid for the lifetime of the application. */
extern NotificationApp* g_notification;
