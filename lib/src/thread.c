// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <chiaki/thread.h>
#include <chiaki/time.h>

#include <stdio.h>
#include <stdlib.h>

static DWORD WINAPI win32_thread_func(LPVOID param)
{
	ChiakiThread *thread = (ChiakiThread *)param;
	thread->ret = thread->func(thread->arg);
	return 0;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_thread_create(ChiakiThread *thread, ChiakiThreadFunc func, void *arg)
{
	thread->func = func;
	thread->arg = arg;
	thread->ret = NULL;
	thread->thread = CreateThread(NULL, 0, win32_thread_func, thread, 0, 0);
	if(!thread->thread)
		return CHIAKI_ERR_THREAD;
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_thread_join(ChiakiThread *thread, void **retval)
{
	int r = WaitForSingleObject(thread->thread, INFINITE);
	if(r != WAIT_OBJECT_0)
		return CHIAKI_ERR_THREAD;
	if(retval)
		*retval = thread->ret;
	return CHIAKI_ERR_SUCCESS;
}

//#define CHIAKI_WINDOWS_THREAD_NAME

CHIAKI_EXPORT ChiakiErrorCode chiaki_thread_set_name(ChiakiThread *thread, const char *name)
{
#ifdef CHIAKI_WINDOWS_THREAD_NAME
	int len = MultiByteToWideChar(CP_UTF8, 0, name, -1, NULL, 0);
	wchar_t *wstr = calloc(sizeof(wchar_t), len+1);
	if(!wstr)
		return CHIAKI_ERR_MEMORY;
	MultiByteToWideChar(CP_UTF8, 0, name, -1, wstr, len);
	SetThreadDescription(thread->thread, wstr);
	free(wstr);
#else
	(void)thread;
	(void)name;
#endif
	return CHIAKI_ERR_SUCCESS;
}

static ChiakiThreadAffinityFunc g_affinity_cb = NULL;
static void *g_affinity_cb_user = NULL;

CHIAKI_EXPORT void chiaki_thread_set_affinity(ChiakiThreadName name)
{
	if(g_affinity_cb)
		g_affinity_cb(name, g_affinity_cb_user);
}

CHIAKI_EXPORT void chiaki_thread_set_affinity_cb(ChiakiThreadAffinityFunc func, void *user)
{
	g_affinity_cb = func;
	g_affinity_cb_user = user;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_mutex_init(ChiakiMutex *mutex, bool rec)
{
	InitializeCriticalSection(&mutex->cs);
	(void)rec; // always recursive
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_mutex_fini(ChiakiMutex *mutex)
{
	DeleteCriticalSection(&mutex->cs);
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_mutex_lock(ChiakiMutex *mutex)
{
	EnterCriticalSection(&mutex->cs);
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_mutex_trylock(ChiakiMutex *mutex)
{
	int r = TryEnterCriticalSection(&mutex->cs);
	if(!r)
		return CHIAKI_ERR_MUTEX_LOCKED;
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_mutex_unlock(ChiakiMutex *mutex)
{
	LeaveCriticalSection(&mutex->cs);
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_cond_init(ChiakiCond *cond)
{
	InitializeConditionVariable(&cond->cond);
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_cond_fini(ChiakiCond *cond)
{
	(void)cond;
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_cond_wait(ChiakiCond *cond, ChiakiMutex *mutex)
{
	int r = SleepConditionVariableCS(&cond->cond, &mutex->cs, INFINITE);
	if(!r)
		return CHIAKI_ERR_THREAD;
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_thread_timedjoin(ChiakiThread *thread, void **retval, uint64_t timeout_ms)
{
	int r = WaitForSingleObject(thread->thread, timeout_ms);
	if(r != WAIT_OBJECT_0)
		return CHIAKI_ERR_THREAD;
	if(retval)
		*retval = thread->ret;
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_cond_timedwait(ChiakiCond *cond, ChiakiMutex *mutex, uint64_t timeout_ms)
{
	int r = SleepConditionVariableCS(&cond->cond, &mutex->cs, (DWORD)timeout_ms);
	if(!r)
	{
		if(GetLastError() == ERROR_TIMEOUT)
			return CHIAKI_ERR_TIMEOUT;
		return CHIAKI_ERR_THREAD;
	}
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_cond_wait_pred(ChiakiCond *cond, ChiakiMutex *mutex, ChiakiCheckPred check_pred, void *check_pred_user)
{
	while(!check_pred(check_pred_user))
	{
		ChiakiErrorCode err = chiaki_cond_wait(cond, mutex);
		if(err != CHIAKI_ERR_SUCCESS)
			return err;
	}
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_cond_timedwait_pred(ChiakiCond *cond, ChiakiMutex *mutex, uint64_t timeout_ms, ChiakiCheckPred check_pred, void *check_pred_user)
{
	uint64_t start_time = chiaki_time_now_monotonic_ms();
	uint64_t elapsed = 0;
	while(!check_pred(check_pred_user))
	{
		ChiakiErrorCode err = chiaki_cond_timedwait(cond, mutex, timeout_ms - elapsed);
		if(err != CHIAKI_ERR_SUCCESS)
			return err;
		elapsed = chiaki_time_now_monotonic_ms() - start_time;
		if(elapsed >= timeout_ms)
			return CHIAKI_ERR_TIMEOUT;
	}
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_cond_signal(ChiakiCond *cond)
{
	WakeConditionVariable(&cond->cond);
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_cond_broadcast(ChiakiCond *cond)
{
	WakeAllConditionVariable(&cond->cond);
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_bool_pred_cond_init(ChiakiBoolPredCond *cond)
{
	cond->pred = false;

	ChiakiErrorCode err = chiaki_mutex_init(&cond->mutex, false);
	if(err != CHIAKI_ERR_SUCCESS)
		return err;

	err = chiaki_cond_init(&cond->cond);
	if(err != CHIAKI_ERR_SUCCESS)
	{
		chiaki_mutex_fini(&cond->mutex);
		return err;
	}

	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_bool_pred_cond_fini(ChiakiBoolPredCond *cond)
{
	ChiakiErrorCode err = chiaki_cond_fini(&cond->cond);
	if(err != CHIAKI_ERR_SUCCESS)
		return err;

	err = chiaki_mutex_fini(&cond->mutex);
	if(err != CHIAKI_ERR_SUCCESS)
		return err;

	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_bool_pred_cond_lock(ChiakiBoolPredCond *cond)
{
	return chiaki_mutex_lock(&cond->mutex);
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_bool_pred_cond_unlock(ChiakiBoolPredCond *cond)
{
	return chiaki_mutex_unlock(&cond->mutex);
}

bool bool_pred_cond_check_pred(void *user)
{
	ChiakiBoolPredCond *bool_pred_cond = user;
	return bool_pred_cond->pred;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_bool_pred_cond_wait(ChiakiBoolPredCond *cond)
{
	return chiaki_cond_wait_pred(&cond->cond, &cond->mutex, bool_pred_cond_check_pred, cond);
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_bool_pred_cond_timedwait(ChiakiBoolPredCond *cond, uint64_t timeout_ms)
{
	return chiaki_cond_timedwait_pred(&cond->cond, &cond->mutex, timeout_ms, bool_pred_cond_check_pred, cond);
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_bool_pred_cond_signal(ChiakiBoolPredCond *cond)
{
	ChiakiErrorCode err = chiaki_bool_pred_cond_lock(cond);
	if(err != CHIAKI_ERR_SUCCESS)
		return err;

	cond->pred = true;

	err = chiaki_bool_pred_cond_unlock(cond);
	if(err != CHIAKI_ERR_SUCCESS)
		return err;

	return chiaki_cond_signal(&cond->cond);
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_bool_pred_cond_broadcast(ChiakiBoolPredCond *cond)
{
	ChiakiErrorCode err = chiaki_bool_pred_cond_lock(cond);
	if(err != CHIAKI_ERR_SUCCESS)
		return err;

	cond->pred = true;

	err = chiaki_bool_pred_cond_unlock(cond);
	if(err != CHIAKI_ERR_SUCCESS)
		return err;

	return chiaki_cond_broadcast(&cond->cond);
}
